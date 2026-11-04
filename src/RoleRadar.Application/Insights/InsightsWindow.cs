namespace RoleRadar.Application.Insights;

/// <summary>The two windows the Insights page offers. The wire names are <see cref="InsightsWindows.Last90DaysName"/> and <see cref="InsightsWindows.AllTimeName"/>.</summary>
public enum InsightsWindow
{
    Last90Days,
    AllTime
}

/// <summary>Wire names for <see cref="InsightsWindow"/> (a string enum in the OpenAPI schema). Matching is exact and case-sensitive.</summary>
public static class InsightsWindows
{
    public const string Last90DaysName = "last90Days";
    public const string AllTimeName = "allTime";

    public static IReadOnlyList<string> Names { get; } = [Last90DaysName, AllTimeName];

    public static string ToWireName(InsightsWindow window) => window switch
    {
        InsightsWindow.Last90Days => Last90DaysName,
        InsightsWindow.AllTime => AllTimeName,
        _ => throw new ArgumentOutOfRangeException(nameof(window), window, "Unknown Insights window.")
    };

    public static bool TryParse(string? value, out InsightsWindow window)
    {
        switch (value)
        {
            case Last90DaysName:
                window = InsightsWindow.Last90Days;
                return true;
            case AllTimeName:
                window = InsightsWindow.AllTime;
                return true;
            default:
                window = default;
                return false;
        }
    }
}
