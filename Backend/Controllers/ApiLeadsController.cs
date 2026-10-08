using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/leads")]
[Authorize]
[IgnoreAntiforgeryToken]
public class ApiLeadsController : ControllerBase
{
    private static readonly Dictionary<string, string[]> AllowedTransitions = new(StringComparer.Ordinal)
    {
        ["New"] = ["Contacted", "Qualified", "Unqualified", "Lost"],
        ["Contacted"] = ["Qualified", "Unqualified", "Lost"],
        ["Qualified"] = ["Unqualified", "Lost"],
        ["Unqualified"] = ["New"],
        ["Converted"] = [],
        ["Lost"] = ["New"]
    };

    private readonly ApplicationDbContext _db;
    private readonly AuditService _audit;
    private readonly SalesAssignmentService _assignment;

    public ApiLeadsController(
        ApplicationDbContext db,
        AuditService audit,
        SalesAssignmentService assignment)
    {
        _db = db;
        _audit = audit;
        _assignment = assignment;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<LeadResponse>>> Get(
        string? q, string? status, int page = 1, int pageSize = 25)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(new { message = "Page must be positive and pageSize must be between 1 and 100." });

        var query = _db.VisibleLeads(User).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.LeadName.Contains(q) || x.CompanyName.Contains(q) ||
                x.Email.Contains(q) || x.Phone.Contains(q));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new LeadResponse(x.LeadId, x.LeadCode, x.LeadName, x.Email,
                x.Phone, x.CompanyName, x.Source, x.Status, x.Priority, x.ExpectedValue,
                x.CreatedDate, x.AssignedTo))
            .ToListAsync();
        return Ok(new PagedResponse<LeadResponse>(items, page, pageSize, total));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeadResponse>> Get(int id)
    {
        var lead = await _db.VisibleLeads(User).AsNoTracking().FirstOrDefaultAsync(x => x.LeadId == id);
        return lead is null ? NotFound() : Ok(ToResponse(lead));
    }

    [HttpPost]
    public async Task<ActionResult<LeadResponse>> Post(LeadRequest request)
    {
        if (request.Status != "New")
            return BadRequest(new { message = "New leads must start with the New status." });
        var assignee = await _assignment.ResolveAssigneeAsync(User, request.AssignedTo);
        if (assignee is null)
            return BadRequest(new { message = "Assign the lead to an active Sales Executive." });

        var lead = new Lead
        {
            LeadCode = $"LEAD-{Guid.NewGuid():N}"[..15].ToUpperInvariant(),
            LeadName = request.LeadName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone.Trim(),
            CompanyName = request.CompanyName.Trim(),
            Source = request.Source,
            Status = "New",
            Priority = request.Priority,
            ExpectedValue = request.ExpectedValue,
            Notes = request.Notes,
            AssignedTo = assignee,
            CreatedDate = DateTime.UtcNow
        };
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Lead", lead.LeadId.ToString(), newValue: lead.LeadName);
        return CreatedAtAction(nameof(Get), new { id = lead.LeadId }, ToResponse(lead));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Put(int id, LeadRequest request)
    {
        var lead = await _db.VisibleLeads(User).FirstOrDefaultAsync(x => x.LeadId == id);
        if (lead is null)
            return NotFound();
        if (request.Status != lead.Status &&
            (!AllowedTransitions.TryGetValue(lead.Status, out var allowed) ||
             !allowed.Contains(request.Status, StringComparer.Ordinal)))
            return BadRequest(new { message = "That lead status transition is not allowed." });
        string? assignee = lead.AssignedTo;
        if (User.CanManageTeamRecords())
        {
            assignee = await _assignment.ResolveAssigneeAsync(User, request.AssignedTo);
            if (assignee is null)
                return BadRequest(new { message = "Assign the lead to an active Sales Executive." });
        }

        var oldStatus = lead.Status;
        lead.LeadName = request.LeadName.Trim();
        lead.Email = request.Email.Trim();
        lead.Phone = request.Phone.Trim();
        lead.CompanyName = request.CompanyName.Trim();
        lead.Source = request.Source;
        lead.Status = request.Status;
        lead.Priority = request.Priority;
        lead.ExpectedValue = request.ExpectedValue;
        lead.Notes = request.Notes;
        lead.AssignedTo = assignee;
        lead.ModifiedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Lead", id.ToString(), oldValue: oldStatus, newValue: lead.Status);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var lead = await _db.VisibleLeads(User).FirstOrDefaultAsync(x => x.LeadId == id);
        if (lead is null)
            return NotFound();
        var oldStatus = lead.Status;
        lead.Status = "Lost";
        lead.ModifiedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Deactivate", "Lead", id.ToString(), oldValue: oldStatus, newValue: "Lost");
        return NoContent();
    }

    private static LeadResponse ToResponse(Lead lead) =>
        new(lead.LeadId, lead.LeadCode, lead.LeadName, lead.Email, lead.Phone,
            lead.CompanyName, lead.Source, lead.Status, lead.Priority, lead.ExpectedValue,
            lead.CreatedDate, lead.AssignedTo);
}
