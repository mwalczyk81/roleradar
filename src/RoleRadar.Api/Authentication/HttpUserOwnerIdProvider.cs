using System.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RoleRadar.Domain.Common;

namespace RoleRadar.Api.Authentication;

public sealed class HttpUserOwnerIdProvider(
    IHttpContextAccessor httpContextAccessor,
    IOptions<OwnerSecurityOptions> options) : IOwnerIdProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly OwnerSecurityOptions _options = options.Value;

    public OwnerId GetOwnerId()
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new SecurityException("No active HTTP context available to resolve owner identifier.");

        var user = httpContext.User;
        if (user.Identity is not { IsAuthenticated: true })
        {
            throw new SecurityException("User is not authenticated; unable to resolve owner identifier.");
        }

        var subClaim = user.FindFirst("sub") ?? user.FindFirst(ClaimTypes.NameIdentifier);
        if (subClaim is null || string.IsNullOrWhiteSpace(subClaim.Value))
        {
            throw new SecurityException("User token is missing 'sub' claim; unable to resolve owner identifier.");
        }

        var subject = subClaim.Value.Trim();
        var isAllowed = _options.AllowedSubjects.Any(s => string.Equals(s, subject, StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            throw new SecurityException($"Subject '{subject}' is not authorized as an owner.");
        }

        // Resolves dynamically via Domain OwnerId constants without hardcoding string literals
        return OwnerId.Default;
    }
}
