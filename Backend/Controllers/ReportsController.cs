using AcxiomCRM.Data;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _db;

    public ReportsController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        ViewBag.Customers = await _db.VisibleCustomers(User).CountAsync();
        ViewBag.Leads = await _db.VisibleLeads(User).CountAsync();
        ViewBag.Opportunities = await _db.VisibleOpportunities(User).CountAsync();
        ViewBag.FollowUps = await _db.VisibleFollowUps(User).CountAsync();
        ViewBag.Won = await _db.VisibleOpportunities(User).CountAsync(x => x.Stage == "Won");
        ViewBag.Lost = await _db.VisibleOpportunities(User).CountAsync(x => x.Stage == "Lost");
        var pipelineAmounts = await _db.VisibleOpportunities(User).Where(x => x.Status == "Open")
            .Select(x => x.Amount)
            .ToListAsync();
        ViewBag.Pipeline = pipelineAmounts.Sum();
        return View();
    }

    public async Task<IActionResult> Customers(string? q, string? status, int page = 1, bool csv = false)
    {
        var query = _db.VisibleCustomers(User).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.CustomerName.Contains(q) || x.Email.Contains(q) || x.CompanyName.Contains(q));
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
                "text/csv; charset=utf-8", "customer-report.csv");
        }
        ViewBag.RouteData = new Dictionary<string, string> { ["q"] = q ?? "", ["status"] = status ?? "" };
        return View(await Paginate(query.OrderByDescending(x => x.CreatedDate), page));
    }

    public async Task<IActionResult> Leads(string? q, string? status, int page = 1, bool csv = false)
    {
        var query = _db.VisibleLeads(User).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.LeadName.Contains(q) || x.CompanyName.Contains(q));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        ViewBag.Q = q;
        ViewBag.Status = status;
        if (csv)
        {
            var rows = await query.OrderByDescending(x => x.CreatedDate).ToListAsync();
            return File(CsvExport.Create(rows,
                ("Code", x => x.LeadCode), ("Name", x => x.LeadName),
                ("Company", x => x.CompanyName), ("Source", x => x.Source),
                ("Status", x => x.Status), ("Priority", x => x.Priority),
                ("Expected value", x => x.ExpectedValue),
                ("Created", x => x.CreatedDate.ToString("yyyy-MM-dd"))),
                "text/csv; charset=utf-8", "lead-report.csv");
        }
        ViewBag.RouteData = new Dictionary<string, string> { ["q"] = q ?? "", ["status"] = status ?? "" };
        return View(await Paginate(query.OrderByDescending(x => x.CreatedDate), page));
    }

    public async Task<IActionResult> Opportunities(string? q, string? stage, string? status, int page = 1, bool csv = false)
    {
        IQueryable<AcxiomCRM.Models.Opportunity> query =
            _db.VisibleOpportunities(User).AsNoTracking().Include(x => x.Customer);
        if (!string.IsNullOrWhiteSpace(stage))
            query = query.Where(x => x.Stage == stage);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.OpportunityName.Contains(q) ||
                (x.Customer != null && x.Customer.CustomerName.Contains(q)));
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);
        ViewBag.Q = q;
        ViewBag.Stage = stage;
        ViewBag.Status = status;
        if (csv)
        {
            var rows = await query.OrderByDescending(x => x.CreatedDate).ToListAsync();
            return File(CsvExport.Create(rows,
                ("Name", x => x.OpportunityName), ("Customer", x => x.Customer?.CustomerName),
                ("Stage", x => x.Stage), ("Status", x => x.Status),
                ("Amount", x => x.Amount), ("Probability", x => x.Probability),
                ("Weighted value", x => x.WeightedValue),
                ("Expected close", x => x.ExpectedCloseDate.ToString("yyyy-MM-dd"))),
                "text/csv; charset=utf-8", "opportunity-report.csv");
        }
        ViewBag.RouteData = new Dictionary<string, string>
        {
            ["q"] = q ?? "", ["stage"] = stage ?? "", ["status"] = status ?? ""
        };
        return View(await Paginate(query.OrderByDescending(x => x.CreatedDate), page));
    }

    public async Task<IActionResult> FollowUps(string? status, DateTime? from, DateTime? to, int page = 1, bool csv = false)
    {
        var query = _db.VisibleFollowUps(User).AsNoTracking();
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
                "text/csv; charset=utf-8", "follow-up-report.csv");
        }
        ViewBag.RouteData = new Dictionary<string, string>
        {
            ["status"] = status ?? "",
            ["from"] = from?.ToString("yyyy-MM-dd") ?? "",
            ["to"] = to?.ToString("yyyy-MM-dd") ?? ""
        };
        return View(await Paginate(query.OrderBy(x => x.FollowUpDate), page));
    }

    private async Task<List<T>> Paginate<T>(IOrderedQueryable<T> query, int page)
    {
        const int pageSize = 25;
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        return await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    }

    public async Task<IActionResult> Pipeline()
    {
        var opportunities = _db.VisibleOpportunities(User).AsNoTracking().Where(x => x.Status == "Open");
        var opportunityRows = await opportunities
            .Select(x => new { x.Stage, x.AssignedTo, x.Amount })
            .ToListAsync();
        var stages = opportunityRows.GroupBy(x => x.Stage)
            .Select(group => new { Stage = group.Key, Amount = group.Sum(x => x.Amount), Count = group.Count() })
            .OrderBy(x => x.Stage)
            .ToList();
        var owners = opportunityRows.GroupBy(x => x.AssignedTo)
            .Select(group => new { UserId = group.Key, Amount = group.Sum(x => x.Amount), Count = group.Count() })
            .ToList();
        var ownerIds = owners.Where(x => x.UserId != null).Select(x => x.UserId!).ToArray();
        var names = await _db.Users.Where(x => ownerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName);
        ViewBag.Stages = stages;
        ViewBag.Owners = owners.Select(x => new
        {
            Name = x.UserId != null && names.TryGetValue(x.UserId, out var name) ? name : "Unassigned",
            x.Amount,
            x.Count
        }).OrderByDescending(x => x.Amount).ToList();
        ViewBag.Total = stages.Sum(x => x.Amount);
        return View();
    }

    public async Task<IActionResult> Conversion(DateTime? from, DateTime? to, bool csv = false)
    {
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return BadRequest("The start date must be on or before the end date.");

        var query = _db.VisibleLeads(User).AsNoTracking();
        if (from.HasValue)
            query = query.Where(x => x.CreatedDate >= from.Value.Date);
        if (to.HasValue)
            query = query.Where(x => x.CreatedDate < to.Value.Date.AddDays(1));

        var leads = await query.Select(x => new { x.Status, x.Source, x.AssignedTo }).ToListAsync();
        var ids = leads.Where(x => x.AssignedTo != null).Select(x => x.AssignedTo!).Distinct().ToArray();
        var names = await _db.Users.Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName);

        var converted = leads.Count(x => x.Status == "Converted");
        var vm = new ConversionReportViewModel
        {
            From = from,
            To = to,
            TotalLeads = leads.Count,
            ConvertedLeads = converted,
            NotConvertedLeads = leads.Count - converted,
            ConversionRate = leads.Count == 0 ? 0 : (double)converted / leads.Count * 100,
            BySource = leads.GroupBy(x => x.Source)
                .Select(group => new ConversionReportRow(
                    group.Key, group.Count(), group.Count(x => x.Status == "Converted")))
                .OrderByDescending(x => x.Total)
                .ToList(),
            ByOwner = leads.GroupBy(x => x.AssignedTo)
                .Select(group => new ConversionReportRow(
                    group.Key is not null && names.TryGetValue(group.Key, out var name) ? name : "Unassigned",
                    group.Count(), group.Count(x => x.Status == "Converted")))
                .OrderByDescending(x => x.Total)
                .ToList()
        };
        if (csv)
        {
            var rows = vm.BySource.Select(x => (Category: "Source", x.Name, x.Total, x.Converted, x.Rate))
                .Concat(vm.ByOwner.Select(x => (Category: "Owner", x.Name, x.Total, x.Converted, x.Rate)));
            return File(CsvExport.Create(rows,
                ("Category", x => x.Category), ("Name", x => x.Name),
                ("Total", x => x.Total), ("Converted", x => x.Converted),
                ("Rate (%)", x => x.Rate.ToString("N1"))),
                "text/csv; charset=utf-8", "lead-conversion-report.csv");
        }
        return View(vm);
    }

    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> UserActivity(int page = 1, bool csv = false)
    {
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(x => x.UserId != null)
            .GroupBy(x => new { x.UserId, x.Action })
            .Select(group => new { group.Key.UserId, group.Key.Action, Count = group.Count(), Latest = group.Max(x => x.CreatedDate) })
            .OrderByDescending(x => x.Count)
            .ToListAsync();
        var ids = rows.Select(x => x.UserId!).Distinct().ToArray();
        var users = await _db.Users.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Email);
        ViewBag.UserNames = users;
        var reportRows = rows.Select(x => new
        {
            UserId = x.UserId!,
            UserName = users.TryGetValue(x.UserId!, out var email) ? email : x.UserId!,
            x.Action,
            x.Count,
            x.Latest
        }).ToList();
        if (csv)
            return File(CsvExport.Create(reportRows,
                ("User", x => x.UserName), ("Action", x => x.Action),
                ("Count", x => x.Count), ("Latest", x => x.Latest.ToString("yyyy-MM-dd HH:mm:ss"))),
                "text/csv; charset=utf-8", "user-activity-report.csv");

        const int pageSize = 25;
        var total = reportRows.Count;
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)));
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        ViewBag.RouteData = new Dictionary<string, string>();
        return View(reportRows.Skip((page - 1) * pageSize).Take(pageSize).ToList());
    }
}
