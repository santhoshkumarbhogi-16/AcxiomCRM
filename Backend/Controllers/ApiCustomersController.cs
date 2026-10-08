using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
[IgnoreAntiforgeryToken]
public class ApiCustomersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;
    private readonly SalesAssignmentService _assignment;

    public ApiCustomersController(
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

    [HttpGet]
    public async Task<ActionResult<PagedResponse<CustomerResponse>>> Get(
        string? q, string? status, int page = 1, int pageSize = 25)
    {
        if (page < 1 || pageSize is < 1 or > 100)
            return BadRequest(new { message = "Page must be positive and pageSize must be between 1 and 100." });

        var query = _db.VisibleCustomers(User).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.CustomerName.Contains(q) || x.CompanyName.Contains(q) ||
                x.Email.Contains(q) || x.Phone.Contains(q));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedDate)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new CustomerResponse(x.CustomerId, x.CustomerCode, x.CustomerName,
                x.Email, x.Phone, x.CompanyName, x.Address, x.City, x.State, x.Status,
                x.CreatedDate, x.AssignedTo))
            .ToListAsync();
        return Ok(new PagedResponse<CustomerResponse>(items, page, pageSize, total));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerResponse>> Get(int id)
    {
        var customer = await _db.VisibleCustomers(User).AsNoTracking()
            .FirstOrDefaultAsync(x => x.CustomerId == id);
        return customer is null ? NotFound() : Ok(ToResponse(customer));
    }

    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Post(CustomerRequest request)
    {
        if (await _db.Customers.AnyAsync(x => x.Email == request.Email || x.Phone == request.Phone))
            return Conflict(new { message = "A customer with this email or phone already exists." });

        var assignee = await _assignment.ResolveAssigneeAsync(User, request.AssignedTo);
        if (assignee is null)
            return BadRequest(new { message = "Assign the customer to an active Sales Executive." });
        var userId = _users.GetUserId(User);
        var customer = new Customer
        {
            CustomerCode = $"CUS-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            CustomerName = request.CustomerName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone.Trim(),
            CompanyName = request.CompanyName.Trim(),
            Address = request.Address,
            City = request.City,
            State = request.State,
            Notes = request.Notes,
            Status = request.Status,
            CreatedBy = userId,
            AssignedTo = assignee
        };
        _db.Customers.Add(customer);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Customer", customer.CustomerId.ToString(), newValue: customer.CustomerName);
        return CreatedAtAction(nameof(Get), new { id = customer.CustomerId }, ToResponse(customer));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Put(int id, CustomerRequest request)
    {
        var customer = await _db.VisibleCustomers(User).FirstOrDefaultAsync(x => x.CustomerId == id);
        if (customer is null)
            return NotFound();
        if (await _db.Customers.AnyAsync(x =>
                x.CustomerId != id && (x.Email == request.Email || x.Phone == request.Phone)))
            return Conflict(new { message = "A customer with this email or phone already exists." });

        string? assignee = customer.AssignedTo;
        if (User.CanManageTeamRecords())
        {
            assignee = await _assignment.ResolveAssigneeAsync(User, request.AssignedTo);
            if (assignee is null)
                return BadRequest(new { message = "Assign the customer to an active Sales Executive." });
        }
        var oldStatus = customer.Status;
        customer.CustomerName = request.CustomerName.Trim();
        customer.Email = request.Email.Trim();
        customer.Phone = request.Phone.Trim();
        customer.CompanyName = request.CompanyName.Trim();
        customer.Address = request.Address;
        customer.City = request.City;
        customer.State = request.State;
        customer.Notes = request.Notes;
        customer.Status = request.Status;
        customer.AssignedTo = assignee;
        customer.ModifiedDate = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Update", "Customer", id.ToString(), oldValue: oldStatus, newValue: customer.Status);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await _db.VisibleCustomers(User).FirstOrDefaultAsync(x => x.CustomerId == id);
        if (customer is null)
            return NotFound();
        if (customer.Status != "Inactive")
        {
            customer.Status = "Inactive";
            customer.ModifiedDate = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await _audit.LogAsync("Deactivate", "Customer", id.ToString(), newValue: customer.CustomerName);
        }
        return NoContent();
    }

    private static CustomerResponse ToResponse(Customer customer) =>
        new(customer.CustomerId, customer.CustomerCode, customer.CustomerName, customer.Email,
            customer.Phone, customer.CompanyName, customer.Address, customer.City, customer.State,
            customer.Status, customer.CreatedDate, customer.AssignedTo);
}
