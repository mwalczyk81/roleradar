using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class CriterionVersionValidationTests
{
    [Fact]
    public void Criterion_Create_ValidInputs_InitializesActiveCriterion()
    {
        var id = Guid.CreateVersion7();
        var effectiveFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var criterion = Criterion.Create(
            ownerId: "owner-1",
            key: "min_base_salary",
            category: CriterionCategory.HardSkip,
            value: "180000",
            provenance: ProvenanceKind.Stated,
            effectiveFrom: effectiveFrom,
            id: id);

        Assert.Equal(id, criterion.Id);
        Assert.Equal("owner-1", criterion.OwnerId);
        Assert.Equal("min_base_salary", criterion.Key);
        Assert.Equal(CriterionCategory.HardSkip, criterion.Category);
        Assert.Equal("180000", criterion.Value);
        Assert.Equal(ProvenanceKind.Stated, criterion.Provenance);
        Assert.Equal(effectiveFrom, criterion.EffectiveFrom);
        Assert.Null(criterion.EffectiveTo);
        Assert.True(criterion.IsActive);
        Assert.NotEqual(Guid.Empty, criterion.VersionToken);
    }

    [Fact]
    public void Criterion_Create_ExceedingLengthLimits_ThrowsArgumentException()
    {
        var effectiveFrom = DateTimeOffset.UtcNow;

        Assert.Throws<ArgumentException>(() =>
            Criterion.Create("owner-1", new string('k', 101), CriterionCategory.HardSkip, "val", ProvenanceKind.Stated, effectiveFrom));

        Assert.Throws<ArgumentException>(() =>
            Criterion.Create("owner-1", "key", CriterionCategory.HardSkip, new string('v', 501), ProvenanceKind.Stated, effectiveFrom));

        Assert.Throws<ArgumentException>(() =>
            Criterion.Create("", "key", CriterionCategory.HardSkip, "val", ProvenanceKind.Stated, effectiveFrom));
    }

    [Fact]
    public void Criterion_Supersede_EarlierOrEqualEffectiveFrom_ThrowsArgumentException()
    {
        var t1 = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var criterion = Criterion.Create("owner-1", "min_base_salary", CriterionCategory.HardSkip, "180000", ProvenanceKind.Stated, t1);

        // Equal effectiveFrom
        Assert.Throws<ArgumentException>(() =>
            criterion.Supersede(CriterionCategory.HardSkip, "200000", ProvenanceKind.Stated, t1, now: t1));

        // Earlier effectiveFrom
        var tEarlier = t1.AddDays(-1);
        Assert.Throws<ArgumentException>(() =>
            criterion.Supersede(CriterionCategory.HardSkip, "200000", ProvenanceKind.Stated, tEarlier, now: t1));
    }

    [Fact]
    public void Criterion_Supersede_ValidInputs_ClosesActiveAndReturnsNewVersion()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        var v1 = Criterion.Create("owner-1", "min_base_salary", CriterionCategory.HardSkip, "180000", ProvenanceKind.Stated, t1);
        var initialToken = v1.VersionToken;

        var v2 = v1.Supersede(CriterionCategory.Weight, "200000", ProvenanceKind.Inferred, t2, now);

        // v1 is closed
        Assert.False(v1.IsActive);
        Assert.Equal(t2, v1.EffectiveTo);
        Assert.NotEqual(initialToken, v1.VersionToken);

        // v2 is active
        Assert.True(v2.IsActive);
        Assert.Equal(v1.Key, v2.Key);
        Assert.Equal(CriterionCategory.Weight, v2.Category);
        Assert.Equal("200000", v2.Value);
        Assert.Equal(ProvenanceKind.Inferred, v2.Provenance);
        Assert.Equal(t2, v2.EffectiveFrom);
        Assert.Null(v2.EffectiveTo);
        Assert.NotEqual(Guid.Empty, v2.VersionToken);
        Assert.NotEqual(v1.VersionToken, v2.VersionToken);
    }

    [Fact]
    public void Criterion_Deactivate_ClosesActiveCriterion()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var deactDate = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        var criterion = Criterion.Create("owner-1", "min_base_salary", CriterionCategory.HardSkip, "180000", ProvenanceKind.Stated, t1);
        var initialToken = criterion.VersionToken;

        criterion.Deactivate(deactDate);

        Assert.False(criterion.IsActive);
        Assert.Equal(deactDate, criterion.EffectiveTo);
        Assert.NotEqual(initialToken, criterion.VersionToken);
    }

    [Fact]
    public void Criterion_IsEffectiveAt_PointInTimeEvaluation()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var v1 = Criterion.Create("owner-1", "min_base_salary", CriterionCategory.HardSkip, "180000", ProvenanceKind.Stated, t1);
        var v2 = v1.Supersede(CriterionCategory.Weight, "200000", ProvenanceKind.Inferred, t2, now: t2);

        // Before v1 effectiveFrom
        Assert.False(v1.IsEffectiveAt(new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero)));
        Assert.False(v2.IsEffectiveAt(new DateTimeOffset(2025, 12, 31, 23, 59, 59, TimeSpan.Zero)));

        // During v1 active window
        var midV1 = new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero);
        Assert.True(v1.IsEffectiveAt(midV1));
        Assert.False(v2.IsEffectiveAt(midV1));

        // Exact boundary t2: v1.EffectiveTo == t2 so exclusive, v2.EffectiveFrom == t2 so inclusive
        Assert.False(v1.IsEffectiveAt(t2));
        Assert.True(v2.IsEffectiveAt(t2));

        // During v2 active window
        var midV2 = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        Assert.False(v1.IsEffectiveAt(midV2));
        Assert.True(v2.IsEffectiveAt(midV2));
    }
}
