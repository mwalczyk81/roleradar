using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoleRadar.Cli.Demo;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

/// <summary>
/// Parity tests for feature 014 (ADR 0042, FR-011): the authenticator key and the enrolment URI keep today's format, so
/// an owner's authenticator app and any existing enrolment keep working. They were written and run against the
/// hand-written code first and were green. The swap to Otp.NET changed the URI in one way, recorded in ADR 0042: Otp.NET writes the
/// default parameters out (<c>algorithm=SHA1</c>, <c>digits=6</c>, <c>period=30</c>), which the Key URI Format allows and which
/// match what the platform provider validates. The key format and the rest of the URI are unchanged.
/// </summary>
public sealed class EnrolmentFormatTests
{
    private const string FictionalPassword = "Fict!onal-Passw0rd-1";

    [Fact]
    public void GeneratedKey_IsBase32_And32Characters()
    {
        var key = CredentialGenerator.GenerateAuthenticatorKey();

        Assert.Matches("^[A-Z2-7]{32}$", key);
    }

    [Fact]
    public async Task ProvisionOwnerWithId_ReturnsTheKeyUriFormatUri()
    {
        await using var db = await NewDbAsync();
        var service = OwnerProvisioningService.Create(db);

        var result = await service.ProvisionOwnerWithIdAsync(
            Guid.CreateVersion7(),
            "owner-fiction",
            FictionalPassword,
            "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP",
            TestContext.Current.CancellationToken);

        // Before the swap this was ".../RoleRadar:owner-fiction?secret=...&issuer=RoleRadar" with nothing after the issuer.
        Assert.Equal(
            "otpauth://totp/RoleRadar:owner-fiction?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=RoleRadar&algorithm=SHA1&digits=6&period=30",
            result.OtpAuthUri);
        Assert.Equal("JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP", result.AuthenticatorKey);
    }

    [Fact]
    public async Task ProvisionOwner_GeneratesAKeyAndTheSameUriShape()
    {
        await using var db = await NewDbAsync();
        var service = OwnerProvisioningService.Create(db);

        var result = await service.ProvisionOwnerAsync("owner-fiction", FictionalPassword, TestContext.Current.CancellationToken);

        Assert.Matches("^[A-Z2-7]{32}$", result.AuthenticatorKey);
        Assert.Matches(
            new Regex("^otpauth://totp/RoleRadar:owner-fiction\\?secret=" + Regex.Escape(result.AuthenticatorKey) + "&issuer=RoleRadar&algorithm=SHA1&digits=6&period=30$"),
            result.OtpAuthUri);
    }

    private static async Task<IdentityAppDbContext> NewDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<IdentityAppDbContext>().UseSqlite(connection).Options;
        var db = new IdentityAppDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }
}
