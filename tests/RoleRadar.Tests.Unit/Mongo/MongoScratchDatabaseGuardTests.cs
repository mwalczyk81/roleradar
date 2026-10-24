using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Mongo;

public sealed class MongoScratchDatabaseGuardTests
{
    private const string TargetDb = "roleradar";
    private const string ValidScratchDb = "roleradar_scratch_018f3a5e12347abc890123456789abcd";

    [Theory]
    [InlineData("roleradar_scratch_018f3a5e12347abc890123456789abcd", true)]
    [InlineData("roleradar_scratch_00000000000000000000000000000000", true)]
    [InlineData("roleradar_scratch_ffffffffffffffffffffffffffffffff", true)]
    [InlineData("roleradar", false)]
    [InlineData("roleradar_dev", false)]
    [InlineData("roleradar_scratch_", false)]
    [InlineData("roleradar_scratch_123", false)]
    [InlineData("roleradar_scratch_018F3A5E12347ABC890123456789ABCD", false)] // Uppercase hex rejected
    [InlineData("roleradar_scratch_018f3a5e12347abc890123456789abcd_extra", false)]
    [InlineData("roleradar_scratch_018f3a5e12347abc890123456789abcd\n", false)] // Trailing LF rejected by \z
    [InlineData("roleradar_scratch_018f3a5e12347abc890123456789abcd\r\n", false)] // Trailing CRLF rejected by \z
    [InlineData("other_scratch_018f3a5e12347abc890123456789abcd", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidScratchDatabaseName_ValidatesStrictRegex(string? dbName, bool expectedValid)
    {
        var isValid = MongoScratchDatabaseGuard.IsValidScratchDatabaseName(dbName);
        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void AssertSafeToDrop_WithValidScratchName_Succeeds()
    {
        // Should not throw
        MongoScratchDatabaseGuard.AssertSafeToDrop(ValidScratchDb, TargetDb);
    }

    [Fact]
    public void AssertSafeToDrop_WhenNameMatchesTargetDb_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MongoScratchDatabaseGuard.AssertSafeToDrop(TargetDb, TargetDb));

        Assert.Contains("target", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("roleradar_production")]
    [InlineData("roleradar_test")]
    [InlineData("admin")]
    [InlineData("roleradar_scratch_invalid")]
    public void AssertSafeToDrop_WhenNameDoesNotMatchPattern_ThrowsInvalidOperationException(string invalidDb)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MongoScratchDatabaseGuard.AssertSafeToDrop(invalidDb, TargetDb));

        Assert.Contains("scratch database pattern", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsEligibleForOrphanCleanup_WhenDatabaseIsYoungerThanOneHour_ReturnsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var creationTime = now.AddMinutes(-30); // 30 mins old < 1 hour

        var eligible = MongoScratchDatabaseGuard.IsEligibleForOrphanCleanup(ValidScratchDb, TargetDb, creationTime, now);

        Assert.False(eligible);
    }

    [Fact]
    public void IsEligibleForOrphanCleanup_WhenDatabaseIsOlderThanOneHour_ReturnsTrue()
    {
        var now = DateTimeOffset.UtcNow;
        var creationTime = now.AddMinutes(-75); // 75 mins old > 1 hour

        var eligible = MongoScratchDatabaseGuard.IsEligibleForOrphanCleanup(ValidScratchDb, TargetDb, creationTime, now);

        Assert.True(eligible);
    }

    [Fact]
    public void IsEligibleForOrphanCleanup_WhenDatabaseMatchesTargetDb_AlwaysReturnsFalse()
    {
        var now = DateTimeOffset.UtcNow;
        var creationTime = now.AddHours(-10);

        var eligible = MongoScratchDatabaseGuard.IsEligibleForOrphanCleanup(TargetDb, TargetDb, creationTime, now);

        Assert.False(eligible);
    }

    [Fact]
    public void GenerateScratchDatabaseName_ReturnsValidScratchName()
    {
        var generated = MongoScratchDatabaseGuard.GenerateScratchDatabaseName();

        Assert.StartsWith("roleradar_scratch_", generated);
        Assert.True(MongoScratchDatabaseGuard.IsValidScratchDatabaseName(generated));
    }

    [Fact]
    public void TryExtractCreationTime_FromUuidV7ScratchName_ExtractsAccurateTimestamp()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-2);
        var generated = MongoScratchDatabaseGuard.GenerateScratchDatabaseName();
        var after = DateTimeOffset.UtcNow.AddSeconds(2);

        var extracted = MongoScratchDatabaseGuard.TryExtractCreationTime(generated, out var creationTime);

        Assert.True(extracted);
        Assert.True(creationTime >= before && creationTime <= after);
    }
}
