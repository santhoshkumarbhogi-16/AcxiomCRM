using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/followups")]
[Authorize]
[IgnoreAntiforgeryToken]
public class ApiFollowUpsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly SalesAssignmentService _assignment;

    public ApiFollowUpsController(
        ApplicationDbContext db,
        AuditService audit,
        SalesAssignmentService assignment)
    {
        _db = db;
        _audit = audit;
        _assignment = assignment;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<FollowUpResponse>>> Get(
        string? status, DateTime? from, DateTime? to, int page = 1, int pageSize = 25)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(new { message = "Page must be positive and pageSize must be between 1 and 100." });
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return BadRequest(new { message = "The start date must be on or before the end date." });

        var query = _db.VisibleFollowUps(User).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        if (from.HasValue)
            query = query.Where(x => x.FollowUpDate >= from.Value.Date);
        if (to.HasValue)
            query = query.Where(x => x.FollowUpDate < to.Value.Date.AddDays(1));

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.FollowUpDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new FollowUpResponse(x.FollowUpId, x.CustomerId, x.LeadId,
                x.OpportunityId, x.Subject, x.FollowUpType, x.FollowUpDate, x.Status,
                x.Remarks, x.AssignedTo))
            .ToListAsync();
        return Ok(new PagedResponse<FollowUpResponse>(items, page, pageSize, total));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FollowUpResponse>> GetById(int id)
    {
        var followUp = await _db.VisibleFollowUps(User).AsNoTracking()
            .FirstOrDefaultAsync(x => x.FollowUpId == id);
        return followUp is null
            ? NotFound()
            : Ok(new FollowUpResponse(followUp.FollowUpId, followUp.CustomerId, followUp.LeadId,
                followUp.OpportunityId, followUp.Subject, followUp.FollowUpType, followUp.FollowUpDate,
                followUp.Status, followUp.Remarks, followUp.AssignedTo));
    }

    [HttpPost]
    public async Task<ActionResult<FollowUpResponse>> Post(FollowUpRequest request)
    {
        if (request.Status != "Planned")
            return BadRequest(new { message = "New follow-ups must start as Planned." });
        var validationError = await ValidateRequest(request, requireFutureDate: true);
        if (validationError is not null)
            return BadRequest(new { message = validationError });
        var assignee = await _assignment.ResolveAssigneeAsync(User, request.AssignedTo);
        if (assignee is null)
            return BadRequest(new { message = "Assign the follow-up to an active Sales Executive." });

        var followUp = new FollowUp();
        ApplyRequest(followUp, request, assignee);
        followUp.Status = "Planned";
        _db.FollowUps.Add(followUp);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "FollowUp", followUp.FollowUpId.ToString(), newValue: followUp.Subject);
        var response = new FollowUpResponse(followUp.FollowUpId, followUp.CustomerId, followUp.LeadId,
            followUp.OpportunityId, followUp.Subject, followUp.FollowUpType, followUp.FollowUpDate,
            followUp.Status, followUp.Remarks, followUp.AssignedTo);
        return CreatedAtAction(nameof(GetById), new { id = followUp.FollowUpId }, response);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Put(int id, FollowUpRequest request)
    {
        var followUp = await _db.VisibleFollowUps(User).FirstOrDefaultAsync(x => x.FollowUpId == id);
        if (followUp is null)
            return NotFound();
        if (followUp.Status != "Planned")
            return Conflict(new { message = "Only planned follow-ups can be updated." });

        var validationError = await ValidateRequest(request, requireFutureDate: request.Status == "Planned");
        if (validationError is not null)
            return BadRequest(new { message = validationError });
        if (request.Status == "Missed" && request.FollowUpDate.Date >= DateTime.Today)
            return BadRequest(new { message = "A follow-up can only be marked missed after its scheduled date." });

        string? assignee = followUp.AssignedTo;
        if (User.CanManageTeamRecords())
        {
            assignee = await _assignment.ResolveAssigneeAsync(User, request.AssignedTo);
            if (assignee is null)
                return BadRequest(new { message = "Assign the follow-up to an active Sales Executive." });
        }

        var oldValue = $"{followUp.Subject}; {followUp.FollowUpDate:yyyy-MM-dd}; {followUp.Status}; {followUp.AssignedTo}";
        ApplyRequest(followUp, request, assignee);
        followUp.Status = request.Status;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "FollowUp", id.ToString(), oldValue: oldValue,
            newValue: $"{followUp.Subject}; {followUp.FollowUpDate:yyyy-MM-dd}; {followUp.Status}; {followUp.AssignedTo}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var followUp = await _db.VisibleFollowUps(User).FirstOrDefaultAsync(x => x.FollowUpId == id);
        if (followUp is null)
            return NotFound();
        var oldValue = $"{followUp.Subject}; {followUp.Status}";
        _db.FollowUps.Remove(followUp);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "FollowUp", id.ToString(), oldValue: oldValue);
        return NoContent();
    }

    private async Task<string?> ValidateRequest(FollowUpRequest request, bool requireFutureDate)
    {
        var relatedRecordCount = (request.CustomerId.HasValue ? 1 : 0) +
            (request.LeadId.HasValue ? 1 : 0) + (request.OpportunityId.HasValue ? 1 : 0);
        if (relatedRecordCount != 1)
            return "Select exactly one customer, lead, or opportunity.";
        if (requireFutureDate && request.FollowUpDate.Date < DateTime.Today)
            return "Follow-up date cannot be earlier than today.";

        var customerIsVisible = !request.CustomerId.HasValue ||
            await _db.VisibleCustomers(User).AnyAsync(x => x.CustomerId == request.CustomerId.Value);
        var leadIsVisible = !request.LeadId.HasValue ||
            await _db.VisibleLeads(User).AnyAsync(x => x.LeadId == request.LeadId.Value);
        var opportunityIsVisible = !request.OpportunityId.HasValue ||
            await _db.VisibleOpportunities(User).AnyAsync(x => x.OpportunityId == request.OpportunityId.Value);
        return customerIsVisible && leadIsVisible && opportunityIsVisible
            ? null
            : "The selected record is unavailable.";
    }

    private static void ApplyRequest(FollowUp followUp, FollowUpRequest request, string? assignee)
    {
        followUp.CustomerId = request.CustomerId;
        followUp.LeadId = request.LeadId;
        followUp.OpportunityId = request.OpportunityId;
        followUp.Subject = request.Subject.Trim();
        followUp.FollowUpType = request.FollowUpType;
        followUp.FollowUpDate = request.FollowUpDate.Date;
        followUp.Remarks = request.Remarks;
        followUp.AssignedTo = assignee;
    }
}
