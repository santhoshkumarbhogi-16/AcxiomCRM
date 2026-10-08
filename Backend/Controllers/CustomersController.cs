using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class CustomersController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;
    private readonly SalesAssignmentService _assignment;

    public CustomersController(
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
        var query = _db.VisibleCustomers(User);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.CustomerName.Contains(q) || x.Email.Contains(q) ||
                x.Phone.Contains(q) || x.CompanyName.Contains(q));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        ViewBag.Q = q;
        ViewBag.Status = status;
        if (csv)
        {
            var rows = await query.OrderByDescending(x => x.CreatedDate).ToListAsync();
            return File(CsvExport.Create(rows,
                ("Code", x => x.CustomerCode), ("Name", x => x.CustomerName),
                ("Email", x => x.Email), ("Phone", x => x.Phone),
                ("Company", x => x.CompanyName), ("Status", x => x.Status),
                ("Created", x => x.CreatedDate.ToString("yyyy-MM-dd"))),
                "text/csv; charset=utf-8", "customers.csv");
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
        var model = new Customer();
        if (User.CanManageTeamRecords())
        {
            var salesUsers = await _assignment.GetSalesExecutivesAsync();
            model.AssignedTo = salesUsers.FirstOrDefault()?.Id;
        }
        await PopulateAssignees();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(Customer model)
    {
        var userId = _users.GetUserId(User);
        model.CustomerCode = $"CUS-{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        model.CreatedDate = DateTime.UtcNow;
        model.CreatedBy = userId;
        if (!User.CanManageTeamRecords())
            model.AssignedTo = userId;
        else if (!await _assignment.IsSalesExecutiveAsync(model.AssignedTo))
            ModelState.AddModelError(nameof(model.AssignedTo), "Assign the customer to an active Sales Executive.");
        if (model.Status is not ("Active" or "Inactive"))
            ModelState.AddModelError(nameof(model.Status), "Select a valid customer status.");
        if (await _db.Customers.AnyAsync(x => x.Email == model.Email || x.Phone == model.Phone))
            ModelState.AddModelError(string.Empty, "A customer with this email or phone already exists.");

        if (!ModelState.IsValid)
        {
            await PopulateAssignees();
            return View(model);
        }

        _db.Customers.Add(model);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Customer", model.CustomerId.ToString(), newValue: model.CustomerName);
        TempData["Success"] = "Customer created.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var model = await _db.VisibleCustomers(User).FirstOrDefaultAsync(x => x.CustomerId == id);
        if (model is null)
            return NotFound();
        await PopulateAssignees();
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, Customer input)
    {
        if (id != input.CustomerId)
            return NotFound();

        var customer = await _db.VisibleCustomers(User).FirstOrDefaultAsync(x => x.CustomerId == id);
        if (customer is null)
            return NotFound();

        if (await _db.Customers.AnyAsync(x =>
                x.CustomerId != id && (x.Email == input.Email || x.Phone == input.Phone)))
            ModelState.AddModelError(string.Empty, "A customer with this email or phone already exists.");
        if (input.Status is not ("Active" or "Inactive"))
            ModelState.AddModelError(nameof(input.Status), "Select a valid customer status.");
        if (User.CanManageTeamRecords() && !await _assignment.IsSalesExecutiveAsync(input.AssignedTo))
            ModelState.AddModelError(nameof(input.AssignedTo), "Assign the customer to an active Sales Executive.");

        if (!ModelState.IsValid)
        {
            await PopulateAssignees();
            return View(input);
        }

        var previousStatus = customer.Status;
        var previousAssignee = customer.AssignedTo;
        customer.CustomerName = input.CustomerName;
        customer.Email = input.Email;
        customer.Phone = input.Phone;
        customer.CompanyName = input.CompanyName;
        customer.Address = input.Address;
        customer.City = input.City;
        customer.State = input.State;
        customer.Notes = input.Notes;
        customer.Status = input.Status;
        if (User.CanManageTeamRecords())
            customer.AssignedTo = input.AssignedTo;
        customer.ModifiedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Customer", id.ToString(),
            oldValue: $"{previousStatus}; AssignedTo={previousAssignee}",
            newValue: $"{customer.Status}; AssignedTo={customer.AssignedTo}");
        TempData["Success"] = "Customer updated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(int id)
    {
        var customer = await _db.VisibleCustomers(User).FirstOrDefaultAsync(x => x.CustomerId == id);
        if (customer is null)
            return NotFound();

        var viewModel = new CustomerDetailsViewModel
        {
            Customer = customer,
            Activities = await _db.VisibleActivities(User).Where(x => x.CustomerId == id)
                .OrderByDescending(x => x.ActivityDate).Take(20).ToListAsync(),
            FollowUps = await _db.VisibleFollowUps(User).Where(x => x.CustomerId == id)
                .OrderByDescending(x => x.FollowUpDate).Take(20).ToListAsync(),
            Opportunities = await _db.VisibleOpportunities(User).Where(x => x.CustomerId == id)
                .OrderByDescending(x => x.CreatedDate).Take(20).ToListAsync()
        };
        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await _db.VisibleCustomers(User).FirstOrDefaultAsync(x => x.CustomerId == id);
        if (customer is null)
            return NotFound();

        customer.Status = "Inactive";
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Deactivate", "Customer", id.ToString(), newValue: customer.CustomerName);
        TempData["Success"] = "Customer deactivated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateAssignees()
    {
        ViewBag.SalesExecutives = new SelectList(
            (await _assignment.GetSalesExecutivesAsync()).Select(x => new { x.Id, Name = $"{x.FullName} ({x.Email})" }),
            "Id", "Name");
    }
}
