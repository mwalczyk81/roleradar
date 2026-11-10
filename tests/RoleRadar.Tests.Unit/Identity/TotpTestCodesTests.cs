using System.Text;
using OtpNet;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

/// <summary>Tests of the test-only helper <see cref="TotpTestCodes"/> (feature 014, T003). It starts green: it is the helper's own contract.</summary>
public sealed class TotpTestCodesTests
{
    // Each test uses its own key: the helper tracks used steps per key for the whole process.
    private static string KeyFor(string name) => Base32Encoding.ToString(Encoding.ASCII.GetBytes(name.PadRight(20, 'x')[..20]));

    [Fact]
    public void NextUnused_ReturnsFiveDistinctCodes_ForFiveCallsInOneStep_ThenWaitsForTheNextStep()
    {
        // 2026-11-10T12:00:10Z: 10 seconds into a 30-second step, so 20 seconds remain.
        var key = KeyFor("fictional-first-key");
        var clock = new DateTime(2026, 11, 10, 12, 0, 10, DateTimeKind.Utc);
        var waits = new List<TimeSpan>();
        string Next() => TotpTestCodes.NextUnused(key, () => clock, delay => { waits.Add(delay); clock += delay; });

        var firstFive = new[] { Next(), Next(), Next(), Next(), Next() };

        Assert.Equal(5, firstFive.Distinct().Count());
        Assert.Empty(waits);
        // The first call is the current step (offset 0), the second the next step (+1).
        Assert.Equal(TotpTestCodes.At(key, clock), firstFive[0]);
        Assert.Equal(TotpTestCodes.At(key, clock.AddSeconds(30)), firstFive[1]);

        // The sixth call finds every step in the window used, waits for the next boundary, and returns a code for a step
        // that is new inside the shifted window (here the new current step plus two).
        var sixth = Next();

        var wait = Assert.Single(waits);
        Assert.True(wait >= TimeSpan.FromSeconds(20) && wait < TimeSpan.FromSeconds(21), $"Waited {wait}.");
        Assert.DoesNotContain(sixth, firstFive);
    }

    [Fact]
    public void NextUnused_TracksEachKeySeparately()
    {
        var clock = new DateTime(2026, 11, 10, 12, 0, 10, DateTimeKind.Utc);
        var keyA = KeyFor("fictional-key-aaaa");
        var keyB = KeyFor("fictional-key-bbbb");

        var first = TotpTestCodes.NextUnused(keyA, () => clock);
        var sameStepOtherKey = TotpTestCodes.NextUnused(keyB, () => clock);

        Assert.Equal(TotpTestCodes.At(keyA, clock), first);
        Assert.Equal(TotpTestCodes.At(keyB, clock), sameStepOtherKey);
    }
}
