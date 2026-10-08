using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace AcxiomCRM.Services;

public class ApplicationUserClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    private readonly IdentityOptions _identityOptions;

    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
        _identityOptions = options.Value;
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        var nameClaimType = _identityOptions.ClaimsIdentity.UserNameClaimType;
        var existingNameClaim = identity.FindFirst(nameClaimType);
        if (existingNameClaim is not null)
            identity.RemoveClaim(existingNameClaim);

        identity.AddClaim(new Claim(nameClaimType, user.FullName));
        return identity;
    }
}
