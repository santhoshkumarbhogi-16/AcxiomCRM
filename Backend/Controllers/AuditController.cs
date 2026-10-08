using AcxiomCRM.Data;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin,Manager")]
public class AuditController : Controller
{
    private readonly ApplicationDbContext _db;

    public AuditController(ApplicationDbContext db) => _db = db;

    public async Task<IActionResult> Index(
        string? action,
        string? entity,
        string? userId,
        DateTime? from,
        DateTime? to,
        int page = 1,
        bool csv = false)
    {
        var query = _db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(x => x.Action == action);
        if (!string.IsNullOrWhiteSpace(entity))
            query = query.Where(x => x.EntityName == entity);
        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(x => x.UserId == userId);
        if (from.HasValue)
            query = query.Where(x => x.CreatedDate >= from.Value.Date);
        if (to.HasValue)
            query = query.Where(x => x.CreatedDate < to.Value.Date.AddDays(1));

        ViewBag.Action = action;
        ViewBag.Entity = entity;
        ViewBag.UserId = userId;
        ViewBag.From = from?.ToString("yyyy-MM-dd");
        ViewBag.To = to?.ToString("yyyy-MM-dd");
        ViewBag.Users = await _db.Users.AsNoTracking()
            .OrderBy(x => x.Email)
            .Select(x => new { x.Id, x.Email })
            .ToListAsync();
        ViewBag.Actions = await _db.AuditLogs.AsNoTracking()
            .Select(x => x.Action).Distinct().OrderBy(x => x).ToListAsync();
        ViewBag.Entities = await _db.AuditLogs.AsNoTracking()
            .Select(x => x.EntityName).Distinct().OrderBy(x => x).ToListAsync();
        if (csv)
        {
            var rows = await query.OrderByDescending(x => x.CreatedDate).ToListAsync();
            return File(CsvExport.Create(rows,
                ("Timestamp", x => x.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss")),
                ("User ID", x => x.UserId), ("Action", x => x.Action),
                ("Module", x => x.EntityName), ("Record ID", x => x.RecordId),
                ("Old value", x => x.OldValue), ("New value", x => x.NewValue),
                ("IP address", x => x.IpAddress)),
                "text/csv; charset=utf-8", "audit-log.csv");
        }

        const int pageSize = 25;
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        ViewBag.CurrentPage = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.RouteData = new Dictionary<string, string>
        {
            ["action"] = action ?? "", ["entity"] = entity ?? "",
            ["userId"] = userId ?? "",
            ["from"] = from?.ToString("yyyy-MM-dd") ?? "",
            ["to"] = to?.ToString("yyyy-MM-dd") ?? ""
        };
        return View(await query.OrderByDescending(x => x.CreatedDate)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync());
    }
}
