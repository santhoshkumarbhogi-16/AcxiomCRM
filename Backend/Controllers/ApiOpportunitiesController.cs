using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/opportunities")]
[Authorize]
[IgnoreAntiforgeryToken]
public class ApiOpportunitiesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly SalesAssignmentService _assignment;

    public ApiOpportunitiesController(
        ApplicationDbContext db,
        AuditService audit,
        SalesAssignmentService assignment)
    {
        _db = db;
        _audit = audit;
        _assignment = assignment;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<OpportunityResponse>>> Get(
        string? q, string? stage, string? status, int page = 1, int pageSize = 25)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(new { message = "Page must be positive and pageSize must be between 1 and 100." });

        var query = _db.VisibleOpportunities(User).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.OpportunityName.Contains(q));
        if (!string.IsNullOrWhiteSpace(stage))
            query = query.Where(x => x.Stage == stage);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OpportunityResponse(x.OpportunityId, x.OpportunityName,
                x.CustomerId, x.LeadId, x.Amount, x.Probability, x.Stage, x.ExpectedCloseDate,
                x.Status, x.WeightedValue, x.Source, x.AssignedTo))
            .ToListAsync();
        return Ok(new PagedResponse<OpportunityResponse>(items, page, pageSize, total));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OpportunityResponse>> Get(int id)
    {
        var opportunity = await _db.VisibleOpportunities(User).AsNoTracking()
            .FirstOrDefaultAsync(x => x.OpportunityId == id);
        return opportunity is null ? NotFound() : Ok(ToResponse(opportunity));
    }

    [HttpPost]
    public async Task<ActionResult<OpportunityResponse>> Post(OpportunityRequest request)
    {
        if (!request.CustomerId.HasValue && !request.LeadId.HasValue)
            return BadRequest(new { message = "Link the opportunity to a customer or lead." });
        if (!await RelatedRecordsAreVisible(request.CustomerId, request.LeadId))
            return BadRequest(new { message = "A selected customer or lead is unavailable." });
        if (request.Status == "Open" && request.ExpectedCloseDate.Date < DateTime.Today)
            return BadRequest(new { message = "Expected Close Date cannot be in the past." });
        if ((request.Stage is "Won" or "Lost") && request.Status != "Closed")
            return BadRequest(new { message = "Won or Lost opportunities must be Closed." });
        var assignee = await _assignment.ResolveAssigneeAsync(User, request.AssignedTo);
        if (assignee is null)
            return BadRequest(new { message = "Assign the opportunity to an active Sales Executive." });

        var opportunity = new Opportunity
        {
            OpportunityName = request.OpportunityName.Trim(),
            CustomerId = request.CustomerId,
            LeadId = request.LeadId,
            Amount = request.Amount,
            Probability = request.Probability,
            Stage = request.Stage,
            ExpectedCloseDate = request.ExpectedCloseDate.Date,
            Status = request.Status,
            Source = request.Source,
            Notes = request.Notes,
            AssignedTo = assignee,
            CreatedDate = DateTime.UtcNow
        };
        _db.Opportunities.Add(opportunity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Opportunity", opportunity.OpportunityId.ToString(),
            newValue: opportunity.OpportunityName);
        return CreatedAtAction(nameof(Get), new { id = opportunity.OpportunityId }, ToResponse(opportunity));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Put(int id, OpportunityRequest request)
    {
        var opportunity = await _db.VisibleOpportunities(User).FirstOrDefaultAsync(x => x.OpportunityId == id);
        if (opportunity is null)
            return NotFound();
        if (!request.CustomerId.HasValue && !request.LeadId.HasValue)
            return BadRequest(new { message = "Link the opportunity to a customer or lead." });
        if (!await RelatedRecordsAreVisible(request.CustomerId, request.LeadId))
            return BadRequest(new { message = "A selected customer or lead is unavailable." });
        if (request.Status == "Open" && request.ExpectedCloseDate.Date < DateTime.Today)
            return BadRequest(new { message = "Expected Close Date cannot be in the past." });
        if ((request.Stage is "Won" or "Lost") && request.Status != "Closed")
            return BadRequest(new { message = "Won or Lost opportunities must be Closed." });
        string? assignee = opportunity.AssignedTo;
        if (User.CanManageTeamRecords())
        {
            assignee = await _assignment.ResolveAssigneeAsync(User, request.AssignedTo);
            if (assignee is null)
                return BadRequest(new { message = "Assign the opportunity to an active Sales Executive." });
        }

        var oldValue = $"{opportunity.Stage}/{opportunity.Status}/{opportunity.Amount}";
        opportunity.OpportunityName = request.OpportunityName.Trim();
        opportunity.CustomerId = request.CustomerId;
        opportunity.LeadId = request.LeadId;
        opportunity.Amount = request.Amount;
        opportunity.Probability = request.Probability;
        opportunity.Stage = request.Stage;
        opportunity.ExpectedCloseDate = request.ExpectedCloseDate.Date;
        opportunity.Status = request.Status;
        opportunity.Source = request.Source;
        opportunity.Notes = request.Notes;
        opportunity.AssignedTo = assignee;
        opportunity.ModifiedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Opportunity", id.ToString(), oldValue: oldValue,
            newValue: $"{opportunity.Stage}/{opportunity.Status}/{opportunity.Amount}");
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var opportunity = await _db.VisibleOpportunities(User).FirstOrDefaultAsync(x => x.OpportunityId == id);
        if (opportunity is null)
            return NotFound();
        var name = opportunity.OpportunityName;
        _db.Opportunities.Remove(opportunity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Opportunity", id.ToString(), oldValue: name);
        return NoContent();
    }

    private async Task<bool> RelatedRecordsAreVisible(int? customerId, int? leadId)
    {
        var customerIsVisible = !customerId.HasValue ||
            await _db.VisibleCustomers(User).AnyAsync(x => x.CustomerId == customerId.Value);
        var leadIsVisible = !leadId.HasValue ||
            await _db.VisibleLeads(User).AnyAsync(x => x.LeadId == leadId.Value);
        return customerIsVisible && leadIsVisible;
    }

    private static OpportunityResponse ToResponse(Opportunity opportunity) =>
        new(opportunity.OpportunityId, opportunity.OpportunityName, opportunity.CustomerId,
            opportunity.LeadId, opportunity.Amount, opportunity.Probability, opportunity.Stage,
            opportunity.ExpectedCloseDate, opportunity.Status, opportunity.WeightedValue,
            opportunity.Source, opportunity.AssignedTo);
}
