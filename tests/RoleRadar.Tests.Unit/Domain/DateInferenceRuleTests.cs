using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Domain;

public sealed class DateInferenceRuleTests
{
    private static readonly DateOnly DefaultDate = new(2026, 10, 24);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankOrNull_ReturnsDefaultDateAndDefaultedKind(string? input)
    {
        var result = DateInferenceRule.Resolve(input, DefaultDate);

        Assert.Equal(DefaultDate, result.ResolvedDate);
        Assert.Equal(DateResolutionKind.DefaultedBlank, result.Kind);
        Assert.False(result.IsApproximate);
        Assert.False(result.IsFlagged);
        Assert.Null(result.Note);
    }

    [Fact]
    public void Resolve_FullIsoDate_ReturnsExact()
    {
        var result = DateInferenceRule.Resolve("2026-05-12", DefaultDate);

        Assert.Equal(new DateOnly(2026, 5, 12), result.ResolvedDate);
        Assert.Equal(DateResolutionKind.Exact, result.Kind);
        Assert.False(result.IsApproximate);
        Assert.False(result.IsFlagged);
    }

    [Fact]
    public void Resolve_FullIsoDateWithLeadingTilde_ReturnsApproximateAndFlagged()
    {
        var result = DateInferenceRule.Resolve("~2026-05-12", DefaultDate);

        Assert.Equal(new DateOnly(2026, 5, 12), result.ResolvedDate);
        Assert.Equal(DateResolutionKind.Exact, result.Kind);
        Assert.True(result.IsApproximate);
        Assert.True(result.IsFlagged);
    }

    [Fact]
    public void Resolve_FullIsoDateWithParenthetical_PreservesDateAndRecordsNote()
    {
        var result = DateInferenceRule.Resolve("2026-05-12 (stated date, may differ from submit date)", DefaultDate);

        Assert.Equal(new DateOnly(2026, 5, 12), result.ResolvedDate);
        Assert.Equal(DateResolutionKind.Exact, result.Kind);
        Assert.True(result.IsFlagged);
        Assert.Contains("stated date, may differ from submit date", result.Note);
    }

    [Fact]
    public void Resolve_PartialDateBeforeDefaultDate_DerivesSameYear()
    {
        // 5/15 is before 2026-10-24 -> 2026-05-15
        var result = DateInferenceRule.Resolve("5/15", DefaultDate);

        Assert.Equal(new DateOnly(2026, 5, 15), result.ResolvedDate);
        Assert.Equal(DateResolutionKind.InferredSameYear, result.Kind);
        Assert.False(result.IsFlagged);
    }

    [Fact]
    public void Resolve_PartialDateAfterDefaultDate_RollsBackToPrecedingYear()
    {
        // 12/15 is after 2026-10-24 -> rolls back to 2025-12-15
        var result = DateInferenceRule.Resolve("12/15", DefaultDate);

        Assert.Equal(new DateOnly(2025, 12, 15), result.ResolvedDate);
        Assert.Equal(DateResolutionKind.InferredRollback, result.Kind);
        Assert.False(result.IsFlagged);
    }

    [Fact]
    public void Resolve_PartialDateWithHyphen_ParsesCorrectly()
    {
        // 03-15 before 2026-10-24 -> 2026-03-15
        var result = DateInferenceRule.Resolve("03-15", DefaultDate);

        Assert.Equal(new DateOnly(2026, 3, 15), result.ResolvedDate);
        Assert.Equal(DateResolutionKind.InferredSameYear, result.Kind);
    }

    [Theory]
    [InlineData("2/30")]
    [InlineData("02-31")]
    [InlineData("13/01")]
    [InlineData("04/31")]
    public void Resolve_ImpossibleDate_TreatsAsMissingFlagsAndDefaults(string impossibleDate)
    {
        var result = DateInferenceRule.Resolve(impossibleDate, DefaultDate);

        Assert.Equal(DefaultDate, result.ResolvedDate);
        Assert.Equal(DateResolutionKind.DefaultedImpossible, result.Kind);
        Assert.True(result.IsFlagged);
    }
}
