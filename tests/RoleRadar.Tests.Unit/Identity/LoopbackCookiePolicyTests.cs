using Microsoft.AspNetCore.Http;
using RoleRadar.Identity.Configuration;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

/// <summary>
/// Feature 015 Gate 5 (ADR 0049): over loopback HTTP the browser drops SameSite=None cookies that lack Secure, which broke
/// sign-in in the local AppHost loop. Identity relaxes to Lax only for an http: loopback issuer, and keeps
/// Duende's default (None) everywhere else, including every https issuer. (No environment check: the demo parity tests
/// require production and demo to take the same paths.)
/// </summary>
public sealed class LoopbackCookiePolicyTests
{
    [Theory]
    [InlineData("http://127.0.0.1:15001")]
    [InlineData("http://localhost:15001")]
    [InlineData("http://[::1]:15001")]
    public void AnHttpLoopbackIssuer_UsesLax(string issuer)
    {
        Assert.Equal(SameSiteMode.Lax, LoopbackCookiePolicy.Resolve(issuer));
    }

    [Theory]
    [InlineData("https://identity:5001")]
    [InlineData("https://127.0.0.1:5001")]
    [InlineData("http://identity.example.test:5001")]
    [InlineData("http://10.0.0.5:5001")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not a uri")]
    public void AnythingElse_KeepsTheDefault(string? issuer)
    {
        Assert.Equal(SameSiteMode.Unspecified, LoopbackCookiePolicy.Resolve(issuer));
    }
}
