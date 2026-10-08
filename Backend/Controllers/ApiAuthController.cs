using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Controllers;

[ApiController]
[Route("api/auth")]
[IgnoreAntiforgeryToken]
public class ApiAuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;

    public ApiAuthController(
        SignInManager<ApplicationUser> signIn,
        UserManager<ApplicationUser> users,
        AuditService audit)
    {
        _signIn = signIn;
        _users = users;
        _audit = audit;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            await _audit.LogAsync("LoginFailed", "Authentication",
                newValue: "Invalid API login credentials.", userId: user?.Id);
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var result = await _signIn.PasswordSignInAsync(
            user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            var action = result.IsLockedOut ? "AccountLocked" : "LoginFailed";
            await _audit.LogAsync(action, "Authentication",
                newValue: result.IsLockedOut ? "API account locked after failed login attempts." : "Invalid API password.",
                userId: user.Id);
            return Unauthorized(new { message = result.IsLockedOut ? "Account locked temporarily." : "Invalid credentials." });
        }

        await _audit.LogAsync("LoginSucceeded", "Authentication", userId: user.Id);
        return Ok(new
        {
            user.Id,
            user.Email,
            user.FullName,
            Roles = await _users.GetRolesAsync(user)
        });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync("Logout", "Authentication");
        await _signIn.SignOutAsync();
        return NoContent();
    }

    public sealed class LoginRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }
}
