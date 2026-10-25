using System.Security;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RoleRadar.Api.Authentication;
using RoleRadar.Domain.Common;
using Xunit;

namespace RoleRadar.Tests.Unit.Authentication;

public sealed class HttpUserOwnerIdProviderTests
{
    private const string AllowedSub = "0192e4a1-0000-7000-8000-000000000001";
    private const string NonAllowedSub = "0192e4a1-0000-7000-8000-000000000099";

    [Fact]
    public void GetOwnerId_WhenSubClaimIsInAllowedSubjects_ReturnsDefaultOwnerId()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
            [new Claim("sub", AllowedSub)],
            authenticationType: "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var contextAccessor = new HttpContextAccessor { HttpContext = context };
        var options = Options.Create(new OwnerSecurityOptions
        {
            AllowedSubjects = [AllowedSub, "another-owner-sub"]
        });

        var provider = new HttpUserOwnerIdProvider(contextAccessor, options);

        // Act
        var ownerId = provider.GetOwnerId();

        // Assert
        Assert.Equal(OwnerId.Default, ownerId);
        Assert.Equal(OwnerId.DefaultValue, ownerId.Value);
    }

    [Fact]
    public void GetOwnerId_WhenNameIdentifierClaimIsInAllowedSubjects_ReturnsDefaultOwnerId()
    {
        // Arrange (JWT inbound mapping may map "sub" to ClaimTypes.NameIdentifier)
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, AllowedSub)],
            authenticationType: "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var contextAccessor = new HttpContextAccessor { HttpContext = context };
        var options = Options.Create(new OwnerSecurityOptions
        {
            AllowedSubjects = [AllowedSub]
        });

        var provider = new HttpUserOwnerIdProvider(contextAccessor, options);

        // Act
        var ownerId = provider.GetOwnerId();

        // Assert
        Assert.Equal(OwnerId.Default, ownerId);
    }

    [Fact]
    public void GetOwnerId_WhenSubClaimIsNotInAllowedSubjects_ThrowsSecurityException()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
            [new Claim("sub", NonAllowedSub)],
            authenticationType: "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var contextAccessor = new HttpContextAccessor { HttpContext = context };
        var options = Options.Create(new OwnerSecurityOptions
        {
            AllowedSubjects = [AllowedSub]
        });

        var provider = new HttpUserOwnerIdProvider(contextAccessor, options);

        // Act & Assert
        var ex = Assert.Throws<SecurityException>(() => provider.GetOwnerId());
        Assert.Contains(NonAllowedSub, ex.Message);
    }

    [Fact]
    public void GetOwnerId_WhenHttpContextIsNull_ThrowsSecurityException()
    {
        var contextAccessor = new HttpContextAccessor { HttpContext = null };
        var options = Options.Create(new OwnerSecurityOptions
        {
            AllowedSubjects = [AllowedSub]
        });

        var provider = new HttpUserOwnerIdProvider(contextAccessor, options);

        Assert.Throws<SecurityException>(() => provider.GetOwnerId());
    }

    [Fact]
    public void GetOwnerId_WhenUserIsNotAuthenticated_ThrowsSecurityException()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity()); // unauthenticated

        var contextAccessor = new HttpContextAccessor { HttpContext = context };
        var options = Options.Create(new OwnerSecurityOptions
        {
            AllowedSubjects = [AllowedSub]
        });

        var provider = new HttpUserOwnerIdProvider(contextAccessor, options);

        Assert.Throws<SecurityException>(() => provider.GetOwnerId());
    }

    [Fact]
    public void GetOwnerId_WhenSubClaimIsMissing_ThrowsSecurityException()
    {
        var context = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, "Admin")],
            authenticationType: "TestAuth");
        context.User = new ClaimsPrincipal(identity);

        var contextAccessor = new HttpContextAccessor { HttpContext = context };
        var options = Options.Create(new OwnerSecurityOptions
        {
            AllowedSubjects = [AllowedSub]
        });

        var provider = new HttpUserOwnerIdProvider(contextAccessor, options);

        Assert.Throws<SecurityException>(() => provider.GetOwnerId());
    }
}
