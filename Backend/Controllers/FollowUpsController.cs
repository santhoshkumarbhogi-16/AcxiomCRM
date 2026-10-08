using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AcxiomCRM.Controllers;

[Authorize]
public class FollowUpsController : Controller
{
    private static readonly string[] FollowUpStatuses = ["Planned", "Completed", "Missed", "Cancelled"];
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;
    private readonly SalesAssignmentService _assignment;

    public FollowUpsController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> users,
        AuditService audit,
        SalesAssignmentService assignment)
    {
        _db = db;
        _users = users;
        _audit = audit;
        _assignment = assignment;
    }

    public async Task<IActionResult> Index(string? status, DateTime? from, DateTime? to, int page = 1, bool csv = false)
    {
        var query = _db.VisibleFollowUps(User);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        if (from.HasValue)
            query = query.Where(x => x.FollowUpDate >= from.Value.Date);
        if (to.HasValue)
            query = query.Where(x => x.FollowUpDate < to.Value.Date.AddDays(1));

        ViewBag.Status = status;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");
        if (csv)
        {
            var rows = await query.OrderBy(x => x.FollowUpDate).ToListAsync();
            return File(CsvExport.Create(rows,
                ("Subject", x => x.Subject), ("Type", x => x.FollowUpType),
                ("Date", x => x.FollowUpDate.ToString("yyyy-MM-dd")),
                ("Status", x => x.Status), ("Customer ID", x => x.CustomerId),
                ("Lead ID", x => x.LeadId), ("Opportunity ID", x => x.OpportunityId),
                ("Remarks", x => x.Remarks)),
                "text/csv; charset=utf-8", "follow-ups.csv");
        }

        const int pageSize = 25;
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.RouteData = new Dictionary<string, string>
        {
            ["status"] = status ?? "",
            ["from"] = from?.ToString("yyyy-MM-dd") ?? "",
            ["to"] = to?.ToString("yyyy-MM-dd") ?? ""
        };
        return View(await query.OrderBy(x => x.FollowUpDate)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
    }

    public async Task<IActionResult> Create()
    {
        await PopulateOptions();
        var model = new FollowUp();
        if (User.CanManageTeamRecords())
            model.AssignedTo = (await _assignment.GetSalesExecutivesAsync()).FirstOrDefault()?.Id;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(FollowUp model)
    {
        if (model.FollowUpDate.Date < DateTime.Today)
            ModelState.AddModelError(nameof(model.FollowUpDate), "Follow-up date cannot be earlier than today.");
        if (model.Status != "Planned")
            ModelState.AddModelError(nameof(model.Status), "New follow-ups must start as Planned.");
        var relatedRecordCount = (model.CustomerId.HasValue ? 1 : 0) +
            (model.LeadId.HasValue ? 1 : 0) + (model.OpportunityId.HasValue ? 1 : 0);
        if (relatedRecordCount != 1)
            ModelState.AddModelError(string.Empty, "Select exactly one customer, lead, or opportunity for this follow-up.");
        if (model.CustomerId.HasValue &&
            !await _db.VisibleCustomers(User).AnyAsync(x => x.CustomerId == model.CustomerId.Value))
            ModelState.AddModelError(nameof(model.CustomerId), "Select a customer you are authorized to access.");
        if (model.LeadId.HasValue &&
            !await _db.VisibleLeads(User).AnyAsync(x => x.LeadId == model.LeadId.Value))
            ModelState.AddModelError(nameof(model.LeadId), "Select a lead you are authorized to access.");
        if (model.OpportunityId.HasValue &&
            !await _db.VisibleOpportunities(User).AnyAsync(x => x.OpportunityId == model.OpportunityId.Value))
            ModelState.AddModelError(nameof(model.OpportunityId), "Select an opportunity you are authorized to access.");
        if (User.CanManageTeamRecords() && !await _assignment.IsSalesExecutiveAsync(model.AssignedTo))
            ModelState.AddModelError(nameof(model.AssignedTo), "Assign the follow-up to an active Sales Executive.");

        if (!ModelState.IsValid)
        {
            await PopulateOptions();
            return View(model);
        }

        if (!User.CanManageTeamRecords())
            model.AssignedTo = _users.GetUserId(User);
        _db.FollowUps.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "FollowUp", model.FollowUpId.ToString(), newValue: model.Subject);
        TempData["Success"] = "Follow-up scheduled.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        if (!FollowUpStatuses.Contains(status, StringComparer.Ordinal) || status == "Planned")
            return BadRequest();

        var followUp = await _db.VisibleFollowUps(User).FirstOrDefaultAsync(x => x.FollowUpId == id);
        if (followUp is null)
            return NotFound();
        if (followUp.Status != "Planned")
        {
            TempData["Error"] = "Only planned follow-ups can change status.";
            return RedirectToAction(nameof(Index));
        }
        if (status == "Missed" && followUp.FollowUpDate.Date >= DateTime.Today)
        {
            TempData["Error"] = "A follow-up can only be marked missed after its scheduled date.";
            return RedirectToAction(nameof(Index));
        }

        followUp.Status = status;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(status, "FollowUp", id.ToString(), oldValue: "Planned", newValue: status);
        TempData["Success"] = $"Follow-up marked {status.ToLowerInvariant()}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Reschedule(int id, DateTime followUpDate)
    {
        if (followUpDate.Date < DateTime.Today)
            ModelState.AddModelError(nameof(followUpDate), "Follow-up date cannot be earlier than today.");
        var followUp = await _db.VisibleFollowUps(User).FirstOrDefaultAsync(x => x.FollowUpId == id);
        if (followUp is null)
            return NotFound();
        if (followUp.Status != "Planned")
            ModelState.AddModelError(string.Empty, "Only planned follow-ups can be rescheduled.");
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values
                .SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        var previousDate = followUp.FollowUpDate;
        followUp.FollowUpDate = followUpDate.Date;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Reschedule", "FollowUp", id.ToString(),
            oldValue: previousDate.ToString("yyyy-MM-dd"),
            newValue: followUp.FollowUpDate.ToString("yyyy-MM-dd"));
        TempData["Success"] = "Follow-up rescheduled.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateOptions()
    {
        ViewBag.Customers = new SelectList(
            await _db.VisibleCustomers(User).OrderBy(x => x.CustomerName)
                .Select(x => new { x.CustomerId, x.CustomerName }).ToListAsync(),
            "CustomerId", "CustomerName");
        ViewBag.Leads = new SelectList(
            await _db.VisibleLeads(User).OrderBy(x => x.LeadName)
                .Select(x => new { x.LeadId, x.LeadName }).ToListAsync(),
            "LeadId", "LeadName");
        ViewBag.Opportunities = new SelectList(
            await _db.VisibleOpportunities(User).OrderBy(x => x.OpportunityName)
                .Select(x => new { x.OpportunityId, x.OpportunityName }).ToListAsync(),
            "OpportunityId", "OpportunityName");
        ViewBag.SalesExecutives = new SelectList(
            (await _assignment.GetSalesExecutivesAsync()).Select(x => new { x.Id, Name = $"{x.FullName} ({x.Email})" }),
            "Id", "Name");
    }
}
