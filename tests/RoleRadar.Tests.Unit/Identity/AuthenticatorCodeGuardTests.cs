using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

/// <summary>
/// Feature 014 (ADR 0042, FR-015, data-model.md "Used authenticator code"). The guard remembers that a one-time code was
/// accepted so the same code is rejected a second time. It is called only after the platform provider has accepted a code.
/// </summary>
public sealed class AuthenticatorCodeGuardTests : IDisposable
{
    private const string KeyOne = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    private const string KeyTwo = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"; // gitleaks:allow (public RFC 6238 test vector, not a credential)

    private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"roleradar-014-guard-{Guid.NewGuid():N}.db");
    private bool _migrated;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            var path = _databaseFile + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task FirstAcceptance_IsRecorded_ASecondUseOfTheSameCodeIsAReplay_ADifferentCodeIsNot()
    {
        await using var db = await NewContextAsync();
        var userId = await SeedUserAsync(db);
        var guard = new AuthenticatorCodeGuard(db, TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await guard.TryRecordAsync(userId, KeyOne, "123456", ct));
        Assert.False(await guard.TryRecordAsync(userId, KeyOne, "123456", ct));
        Assert.True(await guard.TryRecordAsync(userId, KeyOne, "654321", ct));
    }

    [Fact]
    public async Task TwoSimultaneousAttemptsWithTheSameCode_ExactlyOneSucceeds()
    {
        await using var seed = await NewContextAsync();
        var userId = await SeedUserAsync(seed);

        // Each round uses a fresh code and two contexts (two connections) on the same database file.
        for (var round = 0; round < 20; round++)
        {
            await using var first = await NewContextAsync();
            await using var second = await NewContextAsync();
            var code = (100000 + round).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var ct = TestContext.Current.CancellationToken;

            var results = await Task.WhenAll(
                Task.Run(() => new AuthenticatorCodeGuard(first, TimeProvider.System).TryRecordAsync(userId, KeyOne, code, ct), ct),
                Task.Run(() => new AuthenticatorCodeGuard(second, TimeProvider.System).TryRecordAsync(userId, KeyOne, code, ct), ct));

            Assert.Equal(1, results.Count(accepted => accepted));
        }
    }

    [Fact]
    public async Task ARestart_NewGuardAndNewContextOnTheSameStore_StillRejectsARecordedCode()
    {
        var userId = Guid.Empty;
        await using (var before = await NewContextAsync())
        {
            userId = await SeedUserAsync(before);
            Assert.True(await new AuthenticatorCodeGuard(before, TimeProvider.System).TryRecordAsync(userId, KeyOne, "246810", TestContext.Current.CancellationToken));
        }

        await using var after = await NewContextAsync();
        Assert.False(await new AuthenticatorCodeGuard(after, TimeProvider.System).TryRecordAsync(userId, KeyOne, "246810", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoredRow_HoldsAnHmacOfTheCode_NotTheCode_AndOwnerIdEqualsUserId()
    {
        await using var db = await NewContextAsync();
        var userId = await SeedUserAsync(db);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

        await new AuthenticatorCodeGuard(db, clock).TryRecordAsync(userId, KeyOne, "135790", TestContext.Current.CancellationToken);

        var row = await db.Set<UsedAuthenticatorCode>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        // data-model.md: CodeHash is "string (64 hex)": the HMAC-SHA-256 of the submitted code, keyed by the user's authenticator key.
        Assert.Matches("^[0-9a-f]{64}$", row.CodeHash);
        Assert.DoesNotContain("135790", row.CodeHash, StringComparison.Ordinal);
        Assert.Equal(
            Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(KeyOne), Encoding.ASCII.GetBytes("135790"))).ToLowerInvariant(),
            row.CodeHash);
        // data-model.md: OwnerId is "Required, equal to the account's user id" (Principle IX).
        Assert.Equal(userId, row.UserId);
        Assert.Equal(userId, row.OwnerId);
        // data-model.md: "ExpiresAtUtc | AcceptedAtUtc plus 5 minutes", both UTC.
        Assert.Equal(TimeSpan.Zero, row.AcceptedAtUtc.Offset);
        Assert.Equal(clock.GetUtcNow(), row.AcceptedAtUtc);
        Assert.Equal(row.AcceptedAtUtc + TimeSpan.FromMinutes(5), row.ExpiresAtUtc);
    }

    [Fact]
    public async Task TheSameCodeForTwoUsers_HasTwoDifferentHashes_AndAChangedKeyDoesNotMatchAnOldRecord()
    {
        await using var db = await NewContextAsync();
        var userA = await SeedUserAsync(db, "owner-a");
        var userB = await SeedUserAsync(db, "owner-b");
        var guard = new AuthenticatorCodeGuard(db, TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await guard.TryRecordAsync(userA, KeyOne, "111111", ct));
        Assert.True(await guard.TryRecordAsync(userB, KeyTwo, "111111", ct));

        var hashes = await db.Set<UsedAuthenticatorCode>().AsNoTracking().Select(r => r.CodeHash).ToListAsync(ct);
        Assert.Equal(2, hashes.Distinct().Count());

        // A key rotation makes the old record unmatchable, which is correct: the old key's codes no longer verify anyway.
        Assert.True(await guard.TryRecordAsync(userA, KeyTwo, "111111", ct));
    }

    [Fact]
    public async Task ExpiredRows_AreRemovedByTheNextInsert()
    {
        await using var db = await NewContextAsync();
        var userId = await SeedUserAsync(db);
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
        var guard = new AuthenticatorCodeGuard(db, clock);
        var ct = TestContext.Current.CancellationToken;

        Assert.True(await guard.TryRecordAsync(userId, KeyOne, "222222", ct));
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Assert.True(await guard.TryRecordAsync(userId, KeyOne, "333333", ct));

        var rows = await db.Set<UsedAuthenticatorCode>().AsNoTracking().ToListAsync(ct);
        var remaining = Assert.Single(rows);
        Assert.Equal(
            Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(KeyOne), Encoding.ASCII.GetBytes("333333"))).ToLowerInvariant(),
            remaining.CodeHash);
        // The expired code can be recorded again: it was removed.
        Assert.True(await guard.TryRecordAsync(userId, KeyOne, "222222", ct));
    }

    [Fact]
    public async Task DeletingTheUser_RemovesItsRows()
    {
        await using var db = await NewContextAsync();
        var userId = await SeedUserAsync(db);
        Assert.True(await new AuthenticatorCodeGuard(db, TimeProvider.System).TryRecordAsync(userId, KeyOne, "444444", TestContext.Current.CancellationToken));

        var user = await db.Users.SingleAsync(u => u.Id == userId, TestContext.Current.CancellationToken);
        db.Users.Remove(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await db.Set<UsedAuthenticatorCode>().AsNoTracking().ToListAsync(TestContext.Current.CancellationToken));
    }

    private async Task<IdentityAppDbContext> NewContextAsync()
    {
        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite($"Data Source={_databaseFile};Pooling=False")
            .Options;
        var db = new IdentityAppDbContext(options);
        if (!_migrated)
        {
            await db.Database.MigrateAsync();
            _migrated = true;
        }

        return db;
    }

    private static async Task<Guid> SeedUserAsync(IdentityAppDbContext db, string name = "owner-fiction")
    {
        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D"),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user.Id;
    }

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
