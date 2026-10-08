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
public class ActivitiesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;
    private readonly SalesAssignmentService _assignment;

    public ActivitiesController(
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

    public async Task<IActionResult> Index(string? q, string? status, DateTime? date, int page = 1, bool csv = false)
    {
        var query = _db.VisibleActivities(User);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.Subject.Contains(q) || x.ActivityType.Contains(q));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        if (date.HasValue)
            query = query.Where(x => x.ActivityDate.Date == date.Value.Date);

        ViewBag.Q = q;
        ViewBag.Status = status;
        ViewBag.Date = date?.ToString("yyyy-MM-dd");
        if (csv)
        {
            var rows = await query.OrderByDescending(x => x.ActivityDate).ToListAsync();
            return File(CsvExport.Create(rows,
                ("Subject", x => x.Subject), ("Type", x => x.ActivityType),
                ("Date", x => x.ActivityDate.ToString("yyyy-MM-dd HH:mm")),
                ("Status", x => x.Status), ("Customer ID", x => x.CustomerId),
                ("Lead ID", x => x.LeadId), ("Opportunity ID", x => x.OpportunityId),
                ("Description", x => x.Description)),
                "text/csv; charset=utf-8", "activities.csv");
        }

        const int pageSize = 25;
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.RouteData = new Dictionary<string, string>
        {
            ["q"] = q ?? "", ["status"] = status ?? "", ["date"] = date?.ToString("yyyy-MM-dd") ?? ""
        };
        return View(await query.OrderByDescending(x => x.ActivityDate)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
    }

    public async Task<IActionResult> Create()
    {
        await PopulateOptions();
        var model = new Activity();
        if (User.CanManageTeamRecords())
            model.AssignedTo = (await _assignment.GetSalesExecutivesAsync()).FirstOrDefault()?.Id;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Activity model)
    {
        await ValidateActivity(model, isNew: true);

        if (!ModelState.IsValid)
        {
            await PopulateOptions();
            return View(model);
        }

        if (!User.CanManageTeamRecords())
            model.AssignedTo = _users.GetUserId(User);
        _db.Activities.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Activity", model.ActivityId.ToString(), newValue: model.Subject);
        TempData["Success"] = "Activity recorded.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var activity = await _db.VisibleActivities(User).FirstOrDefaultAsync(x => x.ActivityId == id);
        if (activity is null)
            return NotFound();
        if (activity.Status != "Open")
            return BadRequest("Only open activities can be edited.");

        await PopulateOptions();
        return View(activity);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, Activity input)
    {
        if (id != input.ActivityId)
            return NotFound();
        var activity = await _db.VisibleActivities(User).FirstOrDefaultAsync(x => x.ActivityId == id);
        if (activity is null)
            return NotFound();
        if (activity.Status != "Open")
            return BadRequest("Only open activities can be edited.");

        await ValidateActivity(input, isNew: false);
        if (!ModelState.IsValid)
        {
            await PopulateOptions();
            return View(input);
        }

        var oldValue = $"{activity.Subject}; {activity.ActivityType}; {activity.Status}";
        activity.Subject = input.Subject.Trim();
        activity.ActivityType = input.ActivityType;
        activity.ActivityDate = input.ActivityDate;
        activity.Description = input.Description;
        activity.CustomerId = input.CustomerId;
        activity.LeadId = input.LeadId;
        activity.OpportunityId = input.OpportunityId;
        if (User.CanManageTeamRecords())
            activity.AssignedTo = input.AssignedTo;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Activity", id.ToString(), oldValue: oldValue,
            newValue: $"{activity.Subject}; {activity.ActivityType}; {activity.Status}");
        TempData["Success"] = "Activity updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var activity = await _db.VisibleActivities(User).FirstOrDefaultAsync(x => x.ActivityId == id);
        if (activity is null)
            return NotFound();
        var oldValue = $"{activity.Subject}; {activity.Status}";
        _db.Activities.Remove(activity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Activity", id.ToString(), oldValue: oldValue);
        TempData["Success"] = "Activity deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateActivity(Activity model, bool isNew)
    {
        if (model.ActivityType is not ("Call" or "Meeting" or "Email" or "Task"))
            ModelState.AddModelError(nameof(model.ActivityType), "Select a valid activity type.");
        if (isNew && model.Status != "Open")
            ModelState.AddModelError(nameof(model.Status), "New activities must start as Open.");

        var relatedRecordCount = (model.CustomerId.HasValue ? 1 : 0) +
            (model.LeadId.HasValue ? 1 : 0) + (model.OpportunityId.HasValue ? 1 : 0);
        if (relatedRecordCount != 1)
            ModelState.AddModelError(string.Empty, "Select exactly one customer, lead, or opportunity for this activity.");
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
            ModelState.AddModelError(nameof(model.AssignedTo), "Assign the activity to an active Sales Executive.");
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

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(int id, string status)
    {
        if (status is not ("Completed" or "Cancelled"))
            return BadRequest();

        var activity = await _db.VisibleActivities(User).FirstOrDefaultAsync(x => x.ActivityId == id);
        if (activity is null)
            return NotFound();
        if (activity.Status != "Open")
            return BadRequest("Only open activities can be updated.");

        activity.Status = status;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(status, "Activity", id.ToString(), oldValue: "Open", newValue: status);
        TempData["Success"] = $"Activity marked {status.ToLowerInvariant()}.";
        return RedirectToAction(nameof(Index));
    }
}
