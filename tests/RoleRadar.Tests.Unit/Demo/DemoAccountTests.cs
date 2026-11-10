using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoleRadar.Cli.Demo;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Unit.Demo;

/// <summary>
/// The demo account has a fixed, non-secret username and user id. Its password and TOTP key are generated and are
/// never part of a return value of the seed step (FR-006, FR-007a). In-memory SQLite only.
/// </summary>
public sealed class DemoAccountTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private IdentityAppDbContext _db = null!;
    private OwnerProvisioningService _service = null!;

    public async ValueTask InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new IdentityAppDbContext(new DbContextOptionsBuilder<IdentityAppDbContext>().UseSqlite(_connection).Options);
        await _db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        _service = OwnerProvisioningService.Create(_db);
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public void GeneratedPasswords_SatisfyTheDefaultIdentityPasswordOptionsAndDiffer()
    {
        var options = new PasswordOptions();
        var first = CredentialGenerator.GeneratePassword();
        var second = CredentialGenerator.GeneratePassword();

        Assert.NotEqual(first, second);
        foreach (var password in new[] { first, second })
        {
            Assert.True(password.Length >= 20 && password.Length >= options.RequiredLength);
            Assert.Contains(password, char.IsUpper);
            Assert.Contains(password, char.IsLower);
            Assert.Contains(password, char.IsDigit);
            Assert.Contains(password, c => !char.IsLetterOrDigit(c));
            Assert.True(password.Distinct().Count() >= options.RequiredUniqueChars);
        }
    }

    [Fact]
    public async Task EnsureAccount_OnEmptyStore_CreatesTheFixedUserWithTwoFactorOn_AndReturnsNoSecret()
    {
        var manager = new DemoAccountManager(_db);

        var outcome = await manager.EnsureAccountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DemoAccountEnsureOutcome.Created, outcome);
        var user = await _db.Users.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DemoAccount.UserId, user.Id.ToString("D"));
        Assert.Equal(DemoAccount.Username, user.UserName);
        Assert.True(user.TwoFactorEnabled);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));
        Assert.False(string.IsNullOrEmpty(await _service.GetAuthenticatorKeyAsync(user.Id, TestContext.Current.CancellationToken)));

        // The outcome type carries no credential: it is an enum.
        Assert.True(typeof(DemoAccountEnsureOutcome).IsEnum);
    }

    [Fact]
    public async Task EnsureAccount_IsIdempotent_AndDoesNotChangeTheStoredCredentials()
    {
        var manager = new DemoAccountManager(_db);
        await manager.EnsureAccountAsync(TestContext.Current.CancellationToken);
        var before = await _db.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        var keyBefore = await _service.GetAuthenticatorKeyAsync(before.Id, TestContext.Current.CancellationToken);

        var outcome = await manager.EnsureAccountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DemoAccountEnsureOutcome.AlreadyExists, outcome);
        var after = await _db.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(keyBefore, await _service.GetAuthenticatorKeyAsync(after.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EnsureAccount_RefusesWhenAnotherUserExists()
    {
        await _service.ProvisionOwnerAsync("someone-else", "Another-Password-1!", TestContext.Current.CancellationToken);

        var outcome = await new DemoAccountManager(_db).EnsureAccountAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DemoAccountEnsureOutcome.RefusedOtherUserExists, outcome);
        Assert.Single(await _db.Users.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetCredentials_ReturnsCredentialsTheRealChecksAccept_AndInvalidatesThePreviousOnes()
    {
        var manager = new DemoAccountManager(_db);
        await manager.EnsureAccountAsync(TestContext.Current.CancellationToken);
        var user = await _db.Users.SingleAsync(TestContext.Current.CancellationToken);
        user.AccessFailedCount = 4;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
        var stampBefore = user.SecurityStamp;
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var first = await manager.ResetCredentialsAsync(TestContext.Current.CancellationToken);
        var second = await manager.ResetCredentialsAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(DemoAccount.Username, second.Username);
        Assert.NotEqual(first.Password, second.Password);
        Assert.NotEqual(first.AuthenticatorKey, second.AuthenticatorKey);
        Assert.StartsWith($"otpauth://totp/RoleRadar:{DemoAccount.Username}?secret={second.AuthenticatorKey}", second.OtpAuthUri, StringComparison.Ordinal);

        var stored = await _db.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        var hasher = new PasswordHasher<IdentityUser<Guid>>();
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(stored, stored.PasswordHash!, second.Password));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.VerifyHashedPassword(stored, stored.PasswordHash!, first.Password));
        Assert.NotEqual(stampBefore, stored.SecurityStamp);
        Assert.True(stored.TwoFactorEnabled);
        Assert.Equal(0, stored.AccessFailedCount);
        Assert.Null(stored.LockoutEnd);

        // The current TOTP code of the new key is accepted by the real verifier; the old key's code is not.
        var oldKeyCode = TotpTestCodes.Current(first.AuthenticatorKey);
        var newKeyCode = TotpTestCodes.Current(second.AuthenticatorKey);
        if (oldKeyCode != newKeyCode)
        {
            Assert.False(await _service.VerifyTotpAndActivateAsync(stored.Id, oldKeyCode, TestContext.Current.CancellationToken));
        }

        Assert.True(await _service.VerifyTotpAndActivateAsync(stored.Id, newKeyCode, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetCredentials_WithoutAnAccount_ReturnsNull()
    {
        Assert.Null(await new DemoAccountManager(_db).ResetCredentialsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CurrentTotp_MatchesTheStoredKey_AndIsNullWithoutAnAccount()
    {
        var manager = new DemoAccountManager(_db);
        Assert.Null(await manager.CurrentTotpAsync(TestContext.Current.CancellationToken));

        var credentials = await Reset(manager);
        var shown = await manager.CurrentTotpAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(shown);
        Assert.Matches("^[0-9]{6}$", shown.Code);
        Assert.InRange(shown.SecondsRemaining, 1, 30);
        Assert.Equal(TotpTestCodes.Current(credentials.AuthenticatorKey), shown.Code);
    }

    [Fact]
    public async Task EnsureAccount_WritesNothingToTheConsole()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var captured = new StringWriter();
        Console.SetOut(captured);
        Console.SetError(captured);
        try
        {
            await new DemoAccountManager(_db).EnsureAccountAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(string.Empty, captured.ToString());
    }

    private static async Task<DemoCredentials> Reset(DemoAccountManager manager)
    {
        await manager.EnsureAccountAsync(TestContext.Current.CancellationToken);
        var credentials = await manager.ResetCredentialsAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(credentials);
        return credentials;
    }
}
