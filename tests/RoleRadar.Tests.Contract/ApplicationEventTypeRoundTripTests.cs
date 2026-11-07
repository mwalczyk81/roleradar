using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Contract;

/// <summary>
/// Feature 008 pinned the nine application event types, with note last, and that the Domain parser round-trips each.
/// The allowed values themselves are now generated into the OpenAPI schema (ADR 0035, OpenApiEnumContractTests);
/// what stays here is the Domain behaviour a schema cannot show: every wire value parses back to the same member.
/// </summary>
public sealed class ApplicationEventTypeRoundTripTests
{
    private static readonly string[] ExpectedEventTypes =
    [
        "applied", "follow_up", "interview", "offer", "offer_accepted", "offer_declined", "rejected", "withdrawn", "note"
    ];

    [Fact]
    public void EveryEventType_RoundTripsThroughTheDomainParser_WithNoteLast()
    {
        var serialized = Enum.GetValues<ApplicationEventType>().Select(v => v.ToSnakeCase()).ToArray();
        Assert.Equal(ExpectedEventTypes, serialized);

        foreach (var value in ExpectedEventTypes)
        {
            Assert.True(TrackingEnumExtensions.TryParseApplicationEventType(value, out var parsed), $"'{value}' does not parse.");
            Assert.Equal(value, parsed.ToSnakeCase());
        }
    }
}
