namespace RoleRadar.Tests.Integration.Insights;

using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Access to <c>contracts/insights-lenses.json</c>: the filters the web lens module emits (written by the web snapshot
/// test) and the counts an independent oracle expects over the fictional seed (written here, with UPDATE_SNAPSHOTS=1).
/// </summary>
internal static class InsightsLensSnapshot
{
    public const string Owner = InsightsSeed.OwnerA;

    public static bool Updating => Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1";

    // Relaxed escaping keeps the single quotes in OData literals readable, matching what the web snapshot test writes.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string FilePath => Path.Combine(RepositoryRoot(), "contracts", "insights-lenses.json");

    public static JsonObject Load()
    {
        if (!File.Exists(FilePath))
        {
            throw new InvalidOperationException(
                "contracts/insights-lenses.json is missing. Generate it: web `UPDATE_SNAPSHOTS=1 pnpm exec vitest run tests/unit/insights-lenses-snapshot.test.ts`, then the .NET InsightsLensesContractTests with UPDATE_SNAPSHOTS=1.");
        }

        return JsonNode.Parse(File.ReadAllText(FilePath))!.AsObject();
    }

    public static void Save(JsonObject snapshot) =>
        File.WriteAllText(FilePath, snapshot.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n");

    public static string Filter(JsonObject snapshot, string section, string key, bool last90Days) =>
        snapshot[section]![key]!["filters"]![last90Days ? "90d" : "all"]!.GetValue<string>();

    public static string Entity(JsonObject snapshot, string section, string key) =>
        snapshot[section]![key]!["entity"]!.GetValue<string>();

    public static string EntitySet(string entity) => entity == "postings" ? "Postings" : "Applications";

    public static int? Expected(JsonObject snapshot, string section, string key, string owner, bool last90Days) =>
        snapshot[section]![key]!["expected"]?[owner]?[last90Days ? "90d" : "all"]?.GetValue<int>();

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "RoleRadar.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing RoleRadar.sln.");
    }
}
