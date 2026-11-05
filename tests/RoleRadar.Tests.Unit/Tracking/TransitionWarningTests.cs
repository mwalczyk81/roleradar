using RoleRadar.Application.Tracking.Commands;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

// Feature 008, T009: the warning codes are a closed set owned by the Application layer.
public sealed class TransitionWarningTests
{
    private static readonly string[] ExpectedCodes = ["application_closed", "application_still_rejected"];

    [Fact]
    public void Codes_AreExactlyTheTwoClosedValues()
    {
        Assert.Equal(ExpectedCodes, TransitionWarnings.Codes.OrderBy(c => c, StringComparer.Ordinal).ToArray());
        Assert.Equal(2, TransitionWarnings.Codes.Count);
        Assert.Equal("application_still_rejected", TransitionWarnings.ApplicationStillRejected);
        Assert.Equal("application_closed", TransitionWarnings.ApplicationClosed);
    }

    [Theory]
    [InlineData("application_still_rejected")]
    [InlineData("application_closed")]
    public void EachCode_HasAFixedNonEmptyMessage_WithNoPostingOrApplicationText(string code)
    {
        var first = TransitionWarnings.Create(code);
        var second = TransitionWarnings.Create(code);

        Assert.Equal(code, first.Code);
        Assert.False(string.IsNullOrWhiteSpace(first.Message));
        Assert.Equal(first.Message, second.Message);
        Assert.DoesNotContain("{", first.Message, StringComparison.Ordinal);
        Assert.True(first.Message.Length <= 120);
    }

    [Fact]
    public void Create_RejectsAnUnknownCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TransitionWarnings.Create("something_else"));
    }
}
