using AcxiomCRM.Data;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _db;

    public DashboardController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(string range = "month", DateTime? from = null, DateTime? to = null)
    {
        if (range is not ("today" or "week" or "month" or "custom" or "all"))
            return BadRequest("Select a valid dashboard date range.");
        if (range == "custom" && (!from.HasValue || !to.HasValue))
            return BadRequest("Select both a start and end date for a custom range.");
        if (range == "custom" && from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            return BadRequest("The start date must be on or before the end date.");

        var today = DateTime.Today;
        DateTime? start = range switch
        {
            "today" => today,
            "week" => StartOfWeek(today),
            "month" => new DateTime(today.Year, today.Month, 1),
            "custom" => from?.Date,
            _ => null
        };
        DateTime? endExclusive = range switch
        {
            "today" => today.AddDays(1),
            "week" => StartOfWeek(today).AddDays(7),
            "month" => new DateTime(today.Year, today.Month, 1).AddMonths(1),
            "custom" when to.HasValue => to.Value.Date.AddDays(1),
            _ => null
        };

        var customers = _db.VisibleCustomers(User);
        var leads = _db.VisibleLeads(User);
        var opportunities = _db.VisibleOpportunities(User);
        var followUps = _db.VisibleFollowUps(User);
        if (start.HasValue && endExclusive.HasValue)
        {
            customers = customers.Where(x => x.CreatedDate >= start.Value && x.CreatedDate < endExclusive.Value);
            leads = leads.Where(x => x.CreatedDate >= start.Value && x.CreatedDate < endExclusive.Value);
            opportunities = opportunities.Where(x => x.CreatedDate >= start.Value && x.CreatedDate < endExclusive.Value);
        }

        var openOpportunityValues = await opportunities.Where(x => x.Status == "Open")
            .Select(x => new { x.Amount, x.Probability })
            .ToListAsync();

        var vm = new DashboardViewModel
        {
            TotalCustomers = await customers.CountAsync(),
            TotalLeads = await leads.CountAsync(),
            OpenLeads = await leads.CountAsync(x =>
                x.Status != "Lost" && x.Status != "Converted" && x.Status != "Unqualified"),
            TotalOpportunities = await opportunities.CountAsync(),
            OpenOpportunities = await opportunities.CountAsync(x => x.Status == "Open"),
            WonOpportunities = await opportunities.CountAsync(x => x.Stage == "Won"),
            LostOpportunities = await opportunities.CountAsync(x => x.Stage == "Lost"),
            PendingFollowUps = await followUps.CountAsync(x => x.Status == "Planned" && x.FollowUpDate >= today),
            OverdueFollowUps = await followUps.CountAsync(x => x.Status == "Planned" && x.FollowUpDate < today),
            PipelineValue = openOpportunityValues.Sum(x => x.Amount),
            WeightedPipeline = openOpportunityValues.Sum(x => x.Amount * x.Probability / 100m),
            Range = range,
            From = from,
            To = to
        };

        if (User.IsInRole("Admin"))
            vm.TotalUsers = await _db.Users.CountAsync();

        var leadStatusRows = await _db.VisibleLeads(User)
            .GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync();
        vm.LeadStatuses = leadStatusRows.ToDictionary(x => x.Status, x => x.Count);

        var stageRows = await _db.VisibleOpportunities(User)
            .Where(x => x.Status == "Open")
            .Select(x => new { x.Stage, x.Amount })
            .ToListAsync();
        vm.OpportunityStages = stageRows
            .GroupBy(x => x.Stage)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.Amount));

        var sixMonthsAgo = new DateTime(today.Year, today.Month, 1).AddMonths(-5);
        var wonDeals = await _db.VisibleOpportunities(User)
            .Where(x => x.Stage == "Won" && x.ExpectedCloseDate >= sixMonthsAgo && x.ExpectedCloseDate < today.AddDays(1))
            .Select(x => new { x.ExpectedCloseDate, x.Amount })
            .ToListAsync();
        vm.MonthlySales = Enumerable.Range(0, 6)
            .Select(offset => sixMonthsAgo.AddMonths(offset))
            .ToDictionary(
                month => month.ToString("MMM yy"),
                month => wonDeals.Where(x => x.ExpectedCloseDate.Year == month.Year && x.ExpectedCloseDate.Month == month.Month)
                    .Sum(x => x.Amount));

        return View(vm);
    }

    private static DateTime StartOfWeek(DateTime date) =>
        date.Date.AddDays(-((int)date.DayOfWeek + 6) % 7);
}
