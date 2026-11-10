using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

/// <summary>
/// Characterization tests for feature 014 (ADR 0042, FR-018). They pin what the ASP.NET Core Identity authenticator
/// provider does today: it reads keys stored in today's format, accepts two steps either side of the current one, accepts a
/// code more than once, and cannot generate a code. They start green on purpose: they document the platform, and a
/// platform upgrade that changes one of these facts must fail here instead of passing silently.
/// </summary>
public sealed class AuthenticatorProviderBehaviorTests
{
    private const string FictionalPassword = "Fict!onal-Passw0rd-1";

    [Fact]
    public async Task KeyInTodaysFormat_IsReadBackUnchanged_AndItsCurrentCodeIsAccepted()
    {
        await using var harness = await Harness.CreateAsync();
        var key = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        var userId = Guid.CreateVersion7();
        await harness.Service.ProvisionOwnerWithIdAsync(userId, "owner-fiction", FictionalPassword, key, TestContext.Current.CancellationToken);

        var user = (await harness.Users.FindByIdAsync(userId.ToString("D")))!;

        // No re-enrolment: the stored key is the very key the platform provider reads.
        Assert.Equal(key, await harness.Users.GetAuthenticatorKeyAsync(user));
        Assert.True(await harness.Users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, TotpTestCodes.Current(key)));
    }

    [Fact]
    public async Task RawProvider_AcceptsTwoStepsEitherSide_AndRejectsThree()
    {
        await using var harness = await Harness.CreateAsync();
        var (user, key) = await harness.ProvisionAsync();

        await TotpTestCodes.WaitForSafeWindowAsync();

        foreach (var offset in new[] { -2, -1, 0, 1, 2 })
        {
            Assert.True(
                await harness.Users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, TotpTestCodes.Current(key, offset)),
                $"The provider should accept the code {offset} steps from the current one.");
        }

        foreach (var offset in new[] { -3, 3 })
        {
            Assert.False(
                await harness.Users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, TotpTestCodes.Current(key, offset)),
                $"The provider should reject the code {offset} steps from the current one.");
        }
    }

    [Fact]
    public async Task RawProvider_AcceptsTheSameCodeRepeatedly()
    {
        await using var harness = await Harness.CreateAsync();
        var (user, key) = await harness.ProvisionAsync();

        await TotpTestCodes.WaitForSafeWindowAsync();
        var code = TotpTestCodes.Current(key);

        // This pins the upstream gap (dotnet/aspnetcore#14423). If it ever fails, upstream added replay protection:
        // re-read ADR 0042 and consider deleting AuthenticatorCodeGuard.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(
                await harness.Users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code),
                $"The provider is expected to accept the same code again (attempt {attempt}).");
        }
    }

    [Fact]
    public async Task RawProvider_CannotGenerateACode()
    {
        await using var harness = await Harness.CreateAsync();
        var (user, _) = await harness.ProvisionAsync();

        var generated = await harness.Users.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider);

        Assert.True(string.IsNullOrEmpty(generated), "The authenticator provider verifies codes but does not generate them.");
    }

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void OtpNet_MatchesRfc6238AppendixBVectors(long unixSeconds, string expectedSixDigits)
    {
        // RFC 6238 Appendix B, SHA-1, secret "12345678901234567890". The RFC lists eight digits; the six-digit code is the last six.
        const string base32OfRfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"; // gitleaks:allow (public RFC 6238 test vector, not a credential)
        var instant = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

        Assert.Equal(expectedSixDigits, TotpTestCodes.At(base32OfRfcSecret, instant));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;

        private Harness(SqliteConnection connection, ServiceProvider services, IdentityAppDbContext db)
        {
            _connection = connection;
            _services = services;
            Db = db;
            Users = services.GetRequiredService<UserManager<IdentityUser<Guid>>>();
            Service = OwnerProvisioningService.Create(db);
        }

        public IdentityAppDbContext Db { get; }

        public UserManager<IdentityUser<Guid>> Users { get; }

        public OwnerProvisioningService Service { get; }

        public static async Task<Harness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<IdentityAppDbContext>().UseSqlite(connection).Options;
            var db = new IdentityAppDbContext(options);
            await db.Database.MigrateAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection(); // AddDefaultTokenProviders registers token providers that need it.
            services.AddSingleton(db);
            services.AddIdentityCore<IdentityUser<Guid>>()
                .AddEntityFrameworkStores<IdentityAppDbContext>()
                .AddDefaultTokenProviders();
            return new Harness(connection, services.BuildServiceProvider(), db);
        }

        public async Task<(IdentityUser<Guid> User, string Key)> ProvisionAsync()
        {
            var key = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
            var userId = Guid.CreateVersion7();
            await Service.ProvisionOwnerWithIdAsync(userId, "owner-fiction", FictionalPassword, key);
            return ((await Users.FindByIdAsync(userId.ToString("D")))!, key);
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
