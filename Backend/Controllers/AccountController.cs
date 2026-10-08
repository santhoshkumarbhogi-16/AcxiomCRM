using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcxiomCRM.Controllers;

public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signIn;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AuditService _audit;

    public AccountController(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, AuditService audit)
    { _signIn = signIn; _users = users; _audit = audit; }

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel());

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await _users.FindByEmailAsync(model.Email);
        if (user == null || !user.IsActive)
        {
            await _audit.LogAsync("LoginFailed", "Authentication", newValue: "Invalid credentials or inactive account.", userId: user?.Id);
            ModelState.AddModelError("", "Invalid credentials or inactive account.");
            return View(model);
        }

        var result = await _signIn.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            await _audit.LogAsync("LoginSucceeded", "Authentication", userId: user.Id);
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);
            return RedirectToAction("Index", "Dashboard");
        }
        await _audit.LogAsync(result.IsLockedOut ? "AccountLocked" : "LoginFailed", "Authentication",
            newValue: result.IsLockedOut ? "Account locked after failed login attempts." : "Invalid password.",
            userId: user.Id);
        ModelState.AddModelError("", result.IsLockedOut ? "Account locked temporarily." : "Invalid credentials.");
        return View(model);
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Register() =>
        _signIn.IsSignedIn(User) ? RedirectToAction("Index", "Dashboard") : View(new RegisterViewModel());

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (_signIn.IsSignedIn(User))
            return RedirectToAction("Index", "Dashboard");
        if (!ModelState.IsValid)
            return View(model);
        if (await _users.FindByEmailAsync(model.Email) != null)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with this email already exists.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName.Trim(),
            EmailConfirmed = true,
            IsActive = true
        };
        var result = await _users.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        var roleResult = await _users.AddToRoleAsync(user, "SalesExecutive");
        if (!roleResult.Succeeded)
        {
            await _users.DeleteAsync(user);
            foreach (var error in roleResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _audit.LogAsync("Register", "User", user.Id, newValue: "SalesExecutive", userId: user.Id);
        await _signIn.SignInAsync(user, isPersistent: false);
        await _audit.LogAsync("LoginSucceeded", "Authentication", userId: user.Id);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet, AllowAnonymous]
    public IActionResult ResetPassword(string? userId, string? token)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            return BadRequest();
        return View(new ResetPasswordViewModel { UserId = userId, Token = token });
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);
        var user = await _users.FindByIdAsync(model.UserId);
        if (user is null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "This password reset link is invalid or expired.");
            return View(model);
        }

        var result = await _users.ResetPasswordAsync(user, model.Token, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }

        await _audit.LogAsync("PasswordReset", "User", user.Id, newValue: "Password reset completed.");
        TempData["Success"] = "Your password has been changed. Sign in with the new password.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync("Logout", "Authentication");
        await _signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    public IActionResult AccessDenied() => View();
}