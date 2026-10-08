using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.EnsureCreatedAsync();
        await SqliteSchemaUpgrade.ApplyAsync(db);

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { "Admin", "Manager", "SalesExecutive" })
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded)
                    throw new InvalidOperationException($"Could not create the {role} role: {string.Join(", ", result.Errors.Select(x => x.Description))}");
            }

        await CreateUser(userManager, "admin@acxiom.local", "Admin@123", "System Admin", "Admin");
        await CreateUser(userManager, "manager@acxiom.local", "Manager@123", "Sales Manager", "Manager");
        await CreateUser(userManager, "sales@acxiom.local", "Sales@123", "Sales Executive", "SalesExecutive");
    }

    private static async Task CreateUser(UserManager<ApplicationUser> manager, string email, string password, string name, string role)
    {
        var user = await manager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new ApplicationUser { UserName = email, Email = email, FullName = name, EmailConfirmed = true, IsActive = true };
            var result = await manager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not seed {email}: {string.Join(", ", result.Errors.Select(x => x.Description))}");
        }
        if (!await manager.IsInRoleAsync(user, role))
        {
            var roleResult = await manager.AddToRoleAsync(user, role);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException($"Could not assign the {role} role to {email}: {string.Join(", ", roleResult.Errors.Select(x => x.Description))}");
        }
    }
}