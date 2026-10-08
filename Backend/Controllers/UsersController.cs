using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController : Controller
{
    private static readonly string[] AllowedRoles = ["Admin", "Manager", "SalesExecutive"];
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly AuditService _audit;

    public UsersController(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, AuditService audit)
    {
        _users = users;
        _roles = roles;
        _audit = audit;
    }

    public async Task<IActionResult> Index(string? q, int page = 1, bool csv = false)
    {
        var query = _users.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.FullName.Contains(q) || (x.Email != null && x.Email.Contains(q)));

        const int pageSize = 25;
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var orderedQuery = query.OrderBy(x => x.FullName);
        if (!csv)
        {
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.RouteData = new Dictionary<string, string> { ["q"] = q ?? "" };
            orderedQuery = orderedQuery.Skip((page - 1) * pageSize).Take(pageSize).OrderBy(x => x.FullName);
        }
        var users = await orderedQuery.ToListAsync();
        var userRoles = new Dictionary<string, string>();
        foreach (var user in users)
        {
            var roles = await _users.GetRolesAsync(user);
            userRoles[user.Id] = roles.Count > 1 ? "Multiple roles (review)" : roles.FirstOrDefault() ?? "Unassigned";
        }

        if (csv)
        {
            var rows = users.Select(user => (User: user, Role: userRoles[user.Id]));
            return File(CsvExport.Create(rows,
                ("Name", x => x.User.FullName), ("Email", x => x.User.Email),
                ("Role", x => x.Role), ("Active", x => x.User.IsActive),
                ("Lockout end", x => x.User.LockoutEnd?.ToString("yyyy-MM-dd HH:mm:ss"))),
                "text/csv; charset=utf-8", "users.csv");
        }

        ViewBag.Q = q;
        ViewBag.UserRoles = userRoles;
        ViewBag.ResetUrl = TempData["ResetUrl"];
        return View(users);
    }

    public IActionResult Create() => View();

    [HttpPost]
    public async Task<IActionResult> Create(string email, string fullName, string password, string role)
    {
        if (string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            ModelState.AddModelError(nameof(email), "Enter a valid email address.");
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Length > 100)
            ModelState.AddModelError(nameof(fullName), "Enter a name of 1 to 100 characters.");
        if (!AllowedRoles.Contains(role, StringComparer.Ordinal) || !await _roles.RoleExistsAsync(role))
            ModelState.AddModelError(nameof(role), "Select a valid role.");
        if (string.IsNullOrWhiteSpace(password))
            ModelState.AddModelError(nameof(password), "Password is required.");

        if (!ModelState.IsValid)
            return View();
        if (await _users.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(email), "An account with this email already exists.");
            return View();
        }

        var user = new ApplicationUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            FullName = fullName.Trim(),
            EmailConfirmed = true,
            IsActive = true
        };
        var result = await _users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return View();
        }

        var roleResult = await _users.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            await _users.DeleteAsync(user);
            AddErrors(roleResult.Errors);
            return View();
        }

        await _audit.LogAsync("Create", "User", user.Id, newValue: role);
        TempData["Success"] = "User created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ChangeRole(string id, string role)
    {
        if (!AllowedRoles.Contains(role, StringComparer.Ordinal) || !await _roles.RoleExistsAsync(role))
            return BadRequest("Select a valid role.");

        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        var currentRoles = await _users.GetRolesAsync(user);
        if (currentRoles.Count == 1 && currentRoles[0] == role)
            return RedirectToAction(nameof(Index));

        if (user.Id == _users.GetUserId(User) && role != "Admin")
        {
            TempData["Error"] = "You cannot remove your own administrator role.";
            return RedirectToAction(nameof(Index));
        }
        if (currentRoles.Contains("Admin") && role != "Admin" &&
            (await _users.GetUsersInRoleAsync("Admin")).Count <= 1)
        {
            TempData["Error"] = "The last administrator cannot be demoted.";
            return RedirectToAction(nameof(Index));
        }

        if (currentRoles.Count > 0)
        {
            var removeResult = await _users.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                TempData["Error"] = string.Join(" ", removeResult.Errors.Select(x => x.Description));
                return RedirectToAction(nameof(Index));
            }
        }
        var addResult = await _users.AddToRoleAsync(user, role);
        if (!addResult.Succeeded)
        {
            var errors = addResult.Errors.Select(x => x.Description).ToList();
            if (currentRoles.Count > 0)
            {
                var restoreResult = await _users.AddToRolesAsync(user, currentRoles);
                if (!restoreResult.Succeeded)
                    errors.AddRange(restoreResult.Errors.Select(x => $"Role rollback failed: {x.Description}"));
            }
            TempData["Error"] = string.Join(" ", errors);
            return RedirectToAction(nameof(Index));
        }

        var stampResult = await _users.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            TempData["Error"] = string.Join(" ", stampResult.Errors.Select(x => x.Description));
            return RedirectToAction(nameof(Index));
        }
        await _audit.LogAsync("RoleChanged", "User", user.Id,
            oldValue: string.Join(",", currentRoles), newValue: role);
        TempData["Success"] = $"Role updated for {user.Email}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Unlock(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        var lockoutResult = await _users.SetLockoutEndDateAsync(user, null);
        var countResult = await _users.ResetAccessFailedCountAsync(user);
        if (!lockoutResult.Succeeded || !countResult.Succeeded)
        {
            TempData["Error"] = string.Join(" ",
                lockoutResult.Errors.Concat(countResult.Errors).Select(x => x.Description));
            return RedirectToAction(nameof(Index));
        }

        var stampResult = await _users.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            TempData["Error"] = string.Join(" ", stampResult.Errors.Select(x => x.Description));
            return RedirectToAction(nameof(Index));
        }
        await _audit.LogAsync("AccountUnlocked", "User", user.Id, newValue: "Lockout cleared.");
        TempData["Success"] = $"Account unlocked for {user.Email}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> StartPasswordReset(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        var token = await _users.GeneratePasswordResetTokenAsync(user);
        var resetUrl = Url.Action("ResetPassword", "Account",
            new { userId = user.Id, token }, Request.Scheme);
        if (resetUrl is null)
        {
            TempData["Error"] = "Could not create the password reset link.";
            return RedirectToAction(nameof(Index));
        }

        await _audit.LogAsync("PasswordResetRequested", "User", user.Id);
        TempData["ResetUrl"] = resetUrl;
        TempData["Success"] = "Password reset link created. Deliver it to the user through a trusted channel.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Toggle(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null)
            return NotFound();
        if (user.Id == _users.GetUserId(User) && user.IsActive)
        {
            TempData["Error"] = "You cannot deactivate your own account.";
            return RedirectToAction(nameof(Index));
        }
        if (user.IsActive && (await _users.GetRolesAsync(user)).Contains("Admin") &&
            (await _users.GetUsersInRoleAsync("Admin")).Count(x => x.IsActive) <= 1)
        {
            TempData["Error"] = "The last active administrator cannot be deactivated.";
            return RedirectToAction(nameof(Index));
        }

        var oldStatus = user.IsActive ? "Active" : "Inactive";
        user.IsActive = !user.IsActive;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors);
            return RedirectToAction(nameof(Index));
        }

        if (!user.IsActive)
        {
            var stampResult = await _users.UpdateSecurityStampAsync(user);
            if (!stampResult.Succeeded)
            {
                TempData["Error"] = string.Join(" ", stampResult.Errors.Select(x => x.Description));
                return RedirectToAction(nameof(Index));
            }
        }
        await _audit.LogAsync("AccountStatusChanged", "User", user.Id,
            oldValue: oldStatus, newValue: user.IsActive ? "Active" : "Inactive");
        TempData["Success"] = $"Account {user.IsActive switch { true => "activated", false => "deactivated" }}.";
        return RedirectToAction(nameof(Index));
    }

    private void AddErrors(IEnumerable<IdentityError> errors)
    {
        foreach (var error in errors)
            ModelState.AddModelError(string.Empty, error.Description);
    }
}
