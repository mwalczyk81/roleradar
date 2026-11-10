using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>Accept and reject table from specs/013-operability/contracts/http-correlation-and-errors.md.</summary>
public sealed class CorrelationIdTests
{
    [Theory]
    [InlineData("abcdefgh")]
    [InlineData("01970f3e-5b1c-7d2a-9a77-3c2f1e0d4b5a")]
    [InlineData("test-corr-0001")]
    [InlineData("A1.b2_c3-D4")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123")]
    public void TryAccept_AcceptsWellFormedValues(string candidate)
    {
        Assert.True(CorrelationId.TryAccept(candidate, out var accepted));
        Assert.Equal(candidate, accepted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcdefg")]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")]
    [InlineData("-abcdefgh")]
    [InlineData(".abcdefgh")]
    [InlineData("abc defgh")]
    [InlineData("abcdefgh\r\n")]
    [InlineData("abcd\nefgh")]
    [InlineData("abcdefgh%0d%0a")]
    [InlineData("abcdéfghij")]
    [InlineData("<script>alert(1)</script>")]
    public void TryAccept_RejectsMalformedValues(string? candidate)
    {
        Assert.False(CorrelationId.TryAccept(candidate, out var accepted));
        Assert.Null(accepted);
    }

    [Fact]
    public void New_ReturnsAVersion7GuidThatIsAccepted()
    {
        var id = CorrelationId.New();

        Assert.True(Guid.TryParseExact(id, "D", out var guid));
        Assert.Equal(7, guid.Version);
        Assert.True(CorrelationId.TryAccept(id, out _));
    }

    [Fact]
    public void New_ReturnsDistinctValues()
    {
        Assert.NotEqual(CorrelationId.New(), CorrelationId.New());
    }

    [Fact]
    public void HeaderName_IsXCorrelationId()
    {
        Assert.Equal("X-Correlation-ID", CorrelationId.HeaderName);
    }
}
