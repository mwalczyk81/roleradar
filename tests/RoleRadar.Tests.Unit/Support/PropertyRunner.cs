using FsCheck;
using Xunit;

namespace RoleRadar.Tests.Unit.Support;

/// <summary>
/// Runs an FsCheck property with an explicit, bounded configuration (feature 011, story 3). The run count and size are
/// fixed so the Unit lane stays fast. A failure message always carries the seed and shrunk counterexample, and the
/// seed can be replayed by setting <c>ROLERADAR_PROPERTY_SEED</c> to the printed value.
/// </summary>
internal static class PropertyRunner
{
    public const int MaxTest = 150;
    public const int EndSize = 40;
    private const string SeedVariable = "ROLERADAR_PROPERTY_SEED";
    private const ulong Gamma = 0x9E3779B97F4A7C15UL; // FsCheck requires an odd gamma

    /// <summary>
    /// Uniform small codes. FsCheck's own <c>int[]</c> is heavily skewed to zero, which starved the generated documents of
    /// the rare characters (a lone line feed inside quotes) that the properties exist to exercise.
    /// </summary>
    public static Arbitrary<int[]> Codes(int maxInclusive)
        => FsCheck.Fluent.Arb.From(FsCheck.Fluent.Gen.ArrayOf(FsCheck.Fluent.Gen.Choose(0, maxInclusive)));

    public static void Check(Property property)
    {
        var seedText = Environment.GetEnvironmentVariable(SeedVariable);
        var seed = ulong.TryParse(seedText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (ulong)Random.Shared.NextInt64(1, long.MaxValue);

        var config = Config.QuickThrowOnFailure
            .WithMaxTest(MaxTest)
            .WithEndSize(EndSize)
            .WithReplay(seed, Gamma);

        try
        {
            property.Check(config);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"Property failed. Replay with {SeedVariable}={seed}.{Environment.NewLine}{ex.Message}");
        }
    }
}
