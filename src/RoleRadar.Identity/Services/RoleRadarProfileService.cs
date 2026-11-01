using System.Security.Claims;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Microsoft.AspNetCore.Identity;

namespace RoleRadar.Identity.Services;

public class RoleRadarProfileService(
    UserManager<IdentityUser<Guid>> userManager,
    IUserClaimsPrincipalFactory<IdentityUser<Guid>> claimsFactory)
    : IProfileService
{
    private readonly UserManager<IdentityUser<Guid>> _userManager = userManager;
    private readonly IUserClaimsPrincipalFactory<IdentityUser<Guid>> _claimsFactory = claimsFactory;

    public async Task GetProfileDataAsync(ProfileDataRequestContext context, CancellationToken ct = default)
    {
        var sub = context.Subject.GetSubjectId();
        if (!string.IsNullOrWhiteSpace(sub))
        {
            var user = await _userManager.FindByIdAsync(sub);
            if (user != null)
            {
                var principal = await _claimsFactory.CreateAsync(user);
                context.AddRequestedClaims(principal.Claims);

                if (!string.IsNullOrWhiteSpace(user.UserName) &&
                    !context.IssuedClaims.Any(c => c.Type == "preferred_username"))
                {
                    context.IssuedClaims.Add(new Claim("preferred_username", user.UserName));
                }
            }
        }
    }

    public async Task IsActiveAsync(IsActiveContext context, CancellationToken ct = default)
    {
        var sub = context.Subject.GetSubjectId();
        var user = await _userManager.FindByIdAsync(sub);
        context.IsActive = user != null && !await _userManager.IsLockedOutAsync(user);
    }
}
