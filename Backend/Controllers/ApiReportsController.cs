using AcxiomCRM.Data;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
[IgnoreAntiforgeryToken]
public class ApiReportsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public ApiReportsController(ApplicationDbContext db) => _db = db;

    [HttpGet("pipeline")]
    public async Task<IActionResult> Pipeline()
    {
        var open = _db.VisibleOpportunities(User).AsNoTracking()
            .Where(x => x.Status == "Open");
        var opportunityRows = await open
            .Select(x => new { x.Stage, x.Amount, x.Probability })
            .ToListAsync();
        var stages = opportunityRows.GroupBy(x => x.Stage)
            .Select(group => new
            {
                Stage = group.Key,
                Count = group.Count(),
                Amount = group.Sum(x => x.Amount),
                WeightedAmount = group.Sum(x => x.Amount * x.Probability / 100m)
            })
            .OrderBy(x => x.Stage)
            .ToList();
        return Ok(new
        {
            TotalAmount = stages.Sum(x => x.Amount),
            WeightedAmount = stages.Sum(x => x.WeightedAmount),
            Stages = stages
        });
    }
}
