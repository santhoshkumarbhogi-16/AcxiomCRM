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
public class LeadsController : Controller
{
    private static readonly string[] LeadStatuses =
        ["New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost"];

    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;
    private readonly SalesAssignmentService _assignment;

    public LeadsController(
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

    public async Task<IActionResult> Index(string? q, string? status, int page = 1, bool csv = false)
    {
        var query = _db.VisibleLeads(User);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.LeadName.Contains(q) || x.CompanyName.Contains(q) ||
                x.Email.Contains(q) || x.Phone.Contains(q));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        ViewBag.Q = q;
        ViewBag.Status = status;
        if (csv)
        {
            var rows = await query.OrderByDescending(x => x.CreatedDate).ToListAsync();
            return File(CsvExport.Create(rows,
                ("Code", x => x.LeadCode), ("Name", x => x.LeadName),
                ("Email", x => x.Email), ("Phone", x => x.Phone),
                ("Company", x => x.CompanyName), ("Source", x => x.Source),
                ("Status", x => x.Status), ("Priority", x => x.Priority),
                ("Expected value", x => x.ExpectedValue),
                ("Created", x => x.CreatedDate.ToString("yyyy-MM-dd"))),
                "text/csv; charset=utf-8", "leads.csv");
        }

        const int pageSize = 25;
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.RouteData = new Dictionary<string, string>
        {
            ["q"] = q ?? "", ["status"] = status ?? ""
        };
        return View(await query.OrderByDescending(x => x.CreatedDate)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
    }

    public async Task<IActionResult> Create()
    {
        var model = new Lead();
        if (User.CanManageTeamRecords())
        {
            var salesUsers = await _assignment.GetSalesExecutivesAsync();
            model.AssignedTo = salesUsers.FirstOrDefault()?.Id;
        }
        await PopulateAssignees();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Lead model)
    {
        model.LeadCode = $"LEAD-{Guid.NewGuid():N}"[..15].ToUpperInvariant();
        model.CreatedDate = DateTime.UtcNow;
        if (!User.CanManageTeamRecords())
            model.AssignedTo = _users.GetUserId(User);
        else if (!await _assignment.IsSalesExecutiveAsync(model.AssignedTo))
            ModelState.AddModelError(nameof(model.AssignedTo), "Assign the lead to an active Sales Executive.");
        ValidateLeadStatus(model.Status, isNew: true, previousStatus: null);
        ValidateLeadFields(model);
        if (!ModelState.IsValid)
        {
            await PopulateAssignees();
            return View(model);
        }

        _db.Leads.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Lead", model.LeadId.ToString(), newValue: model.LeadName);
        TempData["Success"] = "Lead created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var lead = await _db.VisibleLeads(User).FirstOrDefaultAsync(x => x.LeadId == id);
        if (lead is null)
            return NotFound();
        await PopulateAssignees();
        return View(lead);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, Lead input)
    {
        if (id != input.LeadId)
            return NotFound();

        var lead = await _db.VisibleLeads(User).FirstOrDefaultAsync(x => x.LeadId == id);
        if (lead is null)
            return NotFound();

        ValidateLeadStatus(input.Status, isNew: false, previousStatus: lead.Status);
        ValidateLeadFields(input);
        if (User.CanManageTeamRecords() && !await _assignment.IsSalesExecutiveAsync(input.AssignedTo))
            ModelState.AddModelError(nameof(input.AssignedTo), "Assign the lead to an active Sales Executive.");
        if (!ModelState.IsValid)
        {
            await PopulateAssignees();
            return View(input);
        }

        var oldStatus = lead.Status;
        var oldAssignee = lead.AssignedTo;
        lead.LeadName = input.LeadName;
        lead.Email = input.Email;
        lead.Phone = input.Phone;
        lead.CompanyName = input.CompanyName;
        lead.Source = input.Source;
        lead.Status = input.Status;
        lead.Priority = input.Priority;
        lead.Notes = input.Notes;
        lead.ExpectedValue = input.ExpectedValue;
        lead.ModifiedDate = DateTime.UtcNow;
        if (User.CanManageTeamRecords())
            lead.AssignedTo = input.AssignedTo;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Lead", id.ToString(),
            oldValue: $"{oldStatus}; AssignedTo={oldAssignee}",
            newValue: $"{lead.Status}; AssignedTo={lead.AssignedTo}");
        TempData["Success"] = "Lead updated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var lead = await _db.VisibleLeads(User).FirstOrDefaultAsync(x => x.LeadId == id);
        return lead is null ? NotFound() : View(lead);
    }

    [HttpPost]
    public async Task<IActionResult> Convert(int id)
    {
        var lead = await _db.VisibleLeads(User).FirstOrDefaultAsync(x => x.LeadId == id);
        if (lead is null)
            return NotFound();
        if (lead.Status != "Qualified")
        {
            TempData["Error"] = "Only qualified leads can be converted.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (await _db.Customers.AnyAsync(x => x.Email == lead.Email || x.Phone == lead.Phone))
        {
            TempData["Error"] = "A customer with this email or phone already exists. Resolve the duplicate before converting this lead.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var customer = new Customer
        {
            CustomerCode = $"CUS-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            CustomerName = lead.LeadName,
            Email = lead.Email,
            Phone = lead.Phone,
            CompanyName = lead.CompanyName,
            CreatedBy = lead.AssignedTo,
            AssignedTo = lead.AssignedTo,
            CreatedDate = DateTime.UtcNow
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();

        var opportunity = new Opportunity
        {
            OpportunityName = $"{lead.CompanyName} - {lead.LeadName}",
            CustomerId = customer.CustomerId,
            LeadId = lead.LeadId,
            Amount = Math.Max(lead.ExpectedValue, 0.01m),
            Probability = 10,
            Stage = "Qualification",
            ExpectedCloseDate = DateTime.Today.AddDays(30),
            Status = "Open",
            Source = lead.Source,
            AssignedTo = lead.AssignedTo
        };
        _db.Opportunities.Add(opportunity);
        lead.Status = "Converted";
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        await _audit.LogAsync("Convert", "Lead", id.ToString(),
            oldValue: "Qualified", newValue: $"CustomerId={customer.CustomerId}; OpportunityId={opportunity.OpportunityId}");
        TempData["Success"] = "Lead converted to a customer and opportunity.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var lead = await _db.VisibleLeads(User).FirstOrDefaultAsync(x => x.LeadId == id);
        if (lead is null)
            return NotFound();
        var name = lead.LeadName;
        _db.Leads.Remove(lead);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Lead", id.ToString(), oldValue: name);
        TempData["Success"] = "Lead deleted.";
        return RedirectToAction(nameof(Index));
    }

    private void ValidateLeadStatus(string status, bool isNew, string? previousStatus)
    {
        if (!LeadStatuses.Contains(status, StringComparer.Ordinal))
        {
            ModelState.AddModelError(nameof(Lead.Status), "Select a valid lead status.");
            return;
        }

        if (isNew && status != "New")
        {
            ModelState.AddModelError(nameof(Lead.Status), "New leads must start with the New status.");
            return;
        }

        if (!isNew && status != previousStatus && previousStatus is not null &&
            (!AllowedTransitions.TryGetValue(previousStatus, out var allowedStatuses) ||
             !allowedStatuses.Contains(status, StringComparer.Ordinal)))
        {
            ModelState.AddModelError(nameof(Lead.Status), "That lead status transition is not allowed.");
        }
    }

    private void ValidateLeadFields(Lead model)
    {
        if (model.Source is not ("Website" or "Referral" or "Advertisement" or "Event" or "Other"))
            ModelState.AddModelError(nameof(Lead.Source), "Select a valid lead source.");
        if (model.Priority is not ("Low" or "Normal" or "High"))
            ModelState.AddModelError(nameof(Lead.Priority), "Select a valid lead priority.");
    }

    private async Task PopulateAssignees()
    {
        ViewBag.SalesExecutives = new SelectList(
            (await _assignment.GetSalesExecutivesAsync()).Select(x => new { x.Id, Name = $"{x.FullName} ({x.Email})" }),
            "Id", "Name");
    }

    private static readonly Dictionary<string, string[]> AllowedTransitions = new(StringComparer.Ordinal)
    {
        ["New"] = ["Contacted", "Qualified", "Unqualified", "Lost"],
        ["Contacted"] = ["Qualified", "Unqualified", "Lost"],
        ["Qualified"] = ["Unqualified", "Lost"],
        ["Unqualified"] = ["New"],
        ["Converted"] = [],
        ["Lost"] = ["New"]
    };
}
