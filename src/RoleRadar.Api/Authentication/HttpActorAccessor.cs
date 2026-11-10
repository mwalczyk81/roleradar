using System.Security;
using Microsoft.AspNetCore.Http;

namespace RoleRadar.Api.Authentication;

/// <summary>
/// The audit actor for an API write: the authenticated owner's <c>sub</c> claim (feature 013, FR-015, ADR 0041).
/// Scoped, resolved per request. Inbound claim mapping is off, so <c>sub</c> arrives under its own name and is the only source.
/// </summary>
public sealed class HttpActorAccessor(IHttpContextAccessor httpContextAccessor)
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public string Subject
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User
                ?? throw new SecurityException("No active HTTP context available to resolve the audit actor.");

            if (user.Identity is not { IsAuthenticated: true })
            {
                throw new SecurityException("User is not authenticated; unable to resolve the audit actor.");
            }

            var subject = user.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                throw new SecurityException("User token is missing 'sub' claim; unable to resolve the audit actor.");
            }

            return subject.Trim();
        }
    }
}
