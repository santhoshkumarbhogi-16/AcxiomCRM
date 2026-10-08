using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace AcxiomCRM.Services;

public sealed class SalesAssignmentService
{
    private readonly UserManager<ApplicationUser> _users;

    public SalesAssignmentService(UserManager<ApplicationUser> users) => _users = users;

    public async Task<IReadOnlyList<ApplicationUser>> GetSalesExecutivesAsync() =>
        (await _users.GetUsersInRoleAsync("SalesExecutive"))
        .OrderBy(x => x.FullName)
        .ToList();

    public async Task<bool> IsSalesExecutiveAsync(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return false;
        var user = await _users.FindByIdAsync(userId);
        return user is { IsActive: true } && await _users.IsInRoleAsync(user, "SalesExecutive");
    }

    public async Task<string?> ResolveAssigneeAsync(ClaimsPrincipal user, string? requestedUserId)
    {
        if (!user.CanManageTeamRecords())
            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return await IsSalesExecutiveAsync(requestedUserId) ? requestedUserId : null;
    }
}
