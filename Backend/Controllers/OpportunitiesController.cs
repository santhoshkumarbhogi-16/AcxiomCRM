using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class OpportunitiesController : Controller
{
    private static readonly string[] Stages = ["Qualification", "Proposal", "Negotiation", "Won", "Lost"];
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;
    private readonly SalesAssignmentService _assignment;

    public OpportunitiesController(
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

    public async Task<IActionResult> Index(string? q, string? stage, string? status, int page = 1, bool csv = false)
    {
        var query = _db.VisibleOpportunities(User).Include(x => x.Customer).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.OpportunityName.Contains(q) ||
                (x.Customer != null && x.Customer.CustomerName.Contains(q)));
        if (!string.IsNullOrWhiteSpace(stage))
            query = query.Where(x => x.Stage == stage);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        ViewBag.Q = q;
        ViewBag.Stage = stage;
        ViewBag.Status = status;
        if (csv)
        {
            var rows = await query.OrderByDescending(x => x.CreatedDate).ToListAsync();
            return File(CsvExport.Create(rows,
                ("Name", x => x.OpportunityName),
                ("Customer", x => x.Customer?.CustomerName),
                ("Stage", x => x.Stage), ("Status", x => x.Status),
                ("Amount", x => x.Amount), ("Probability", x => x.Probability),
                ("Weighted value", x => x.WeightedValue),
                ("Expected close", x => x.ExpectedCloseDate.ToString("yyyy-MM-dd"))),
                "text/csv; charset=utf-8", "opportunities.csv");
        }

        const int pageSize = 25;
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.RouteData = new Dictionary<string, string>
        {
            ["q"] = q ?? "", ["stage"] = stage ?? "", ["status"] = status ?? ""
        };
        return View(await query.OrderByDescending(x => x.CreatedDate)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
    }

    public async Task<IActionResult> Create()
    {
        await PopulateOptions();
        var model = new Opportunity();
        if (User.CanManageTeamRecords())
            model.AssignedTo = (await _assignment.GetSalesExecutivesAsync()).FirstOrDefault()?.Id;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Opportunity model)
    {
        ValidateOpportunity(model);
        await ValidateCustomer(model.CustomerId);
        await ValidateLead(model.LeadId);
        if (!User.CanManageTeamRecords())
            model.AssignedTo = _users.GetUserId(User);
        else if (!await _assignment.IsSalesExecutiveAsync(model.AssignedTo))
            ModelState.AddModelError(nameof(model.AssignedTo), "Assign the opportunity to an active Sales Executive.");
        if (!ModelState.IsValid)
        {
            await PopulateOptions();
            return View(model);
        }

        model.CreatedDate = DateTime.UtcNow;
        _db.Opportunities.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Opportunity", model.OpportunityId.ToString(), newValue: model.OpportunityName);
        TempData["Success"] = "Opportunity created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var opportunity = await _db.VisibleOpportunities(User).FirstOrDefaultAsync(x => x.OpportunityId == id);
        if (opportunity is null)
            return NotFound();
        await PopulateOptions();
        return View(opportunity);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, Opportunity input)
    {
        if (id != input.OpportunityId)
            return NotFound();

        var opportunity = await _db.VisibleOpportunities(User).FirstOrDefaultAsync(x => x.OpportunityId == id);
        if (opportunity is null)
            return NotFound();

        ValidateOpportunity(input);
        await ValidateCustomer(input.CustomerId);
        await ValidateLead(input.LeadId);
        if (User.CanManageTeamRecords() && !await _assignment.IsSalesExecutiveAsync(input.AssignedTo))
            ModelState.AddModelError(nameof(input.AssignedTo), "Assign the opportunity to an active Sales Executive.");
        if (!ModelState.IsValid)
        {
            await PopulateOptions();
            return View(input);
        }

        var oldValue = $"{opportunity.Stage}/{opportunity.Status}/{opportunity.Amount}";
        var oldAssignee = opportunity.AssignedTo;
        opportunity.OpportunityName = input.OpportunityName;
        opportunity.CustomerId = input.CustomerId;
        opportunity.LeadId = input.LeadId;
        opportunity.Source = input.Source;
        opportunity.Amount = input.Amount;
        opportunity.Probability = input.Probability;
        opportunity.Stage = input.Stage;
        opportunity.ExpectedCloseDate = input.ExpectedCloseDate;
        opportunity.Status = input.Status;
        opportunity.Notes = input.Notes;
        opportunity.ModifiedDate = DateTime.UtcNow;
        if (User.CanManageTeamRecords())
            opportunity.AssignedTo = input.AssignedTo;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Opportunity", id.ToString(),
            oldValue: $"{oldValue}; AssignedTo={oldAssignee}",
            newValue: $"{opportunity.Stage}/{opportunity.Status}/{opportunity.Amount}; AssignedTo={opportunity.AssignedTo}");
        TempData["Success"] = "Opportunity updated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var opportunity = await _db.VisibleOpportunities(User)
            .Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.OpportunityId == id);
        return opportunity is null ? NotFound() : View(opportunity);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var opportunity = await _db.VisibleOpportunities(User).FirstOrDefaultAsync(x => x.OpportunityId == id);
        if (opportunity is null)
            return NotFound();
        var name = opportunity.OpportunityName;
        _db.Opportunities.Remove(opportunity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Opportunity", id.ToString(), oldValue: name);
        TempData["Success"] = "Opportunity deleted.";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateOpportunity(Opportunity model)
    {
        if (model.Amount <= 0)
            ModelState.AddModelError(nameof(model.Amount), "Opportunity Amount must be greater than 0.");
        if (model.Probability is < 0 or > 100)
            ModelState.AddModelError(nameof(model.Probability), "Probability must be between 0 and 100.");
        if (!Stages.Contains(model.Stage, StringComparer.Ordinal))
            ModelState.AddModelError(nameof(model.Stage), "Select a valid opportunity stage.");
        if (model.Status is not ("Open" or "Closed"))
            ModelState.AddModelError(nameof(model.Status), "Select a valid opportunity status.");
        if ((model.Stage is "Won" or "Lost") && model.Status != "Closed")
            ModelState.AddModelError(nameof(model.Status), "Won or Lost opportunities must be Closed.");
        if (model.Status == "Open" && model.ExpectedCloseDate.Date < DateTime.Today)
            ModelState.AddModelError(nameof(model.ExpectedCloseDate), "Expected Close Date cannot be in the past.");
        if (!model.CustomerId.HasValue && !model.LeadId.HasValue)
            ModelState.AddModelError(string.Empty, "Link the opportunity to a customer or lead.");
    }

    private async Task ValidateCustomer(int? customerId)
    {
        if (customerId.HasValue &&
            !await _db.VisibleCustomers(User).AnyAsync(x => x.CustomerId == customerId.Value))
            ModelState.AddModelError(nameof(Opportunity.CustomerId), "Select a customer you are authorized to access.");
    }

    private async Task ValidateLead(int? leadId)
    {
        if (leadId.HasValue &&
            !await _db.VisibleLeads(User).AnyAsync(x => x.LeadId == leadId.Value))
            ModelState.AddModelError(nameof(Opportunity.LeadId), "Select a lead you are authorized to access.");
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
        ViewBag.SalesExecutives = new SelectList(
            (await _assignment.GetSalesExecutivesAsync()).Select(x => new { x.Id, Name = $"{x.FullName} ({x.Email})" }),
            "Id", "Name");
    }
}
