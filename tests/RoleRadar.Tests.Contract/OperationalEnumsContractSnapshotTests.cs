using System.Text.Json;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class OperationalEnumsContractSnapshotTests
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record OperationalEnumsSnapshot(
        IReadOnlyList<string> CriterionCategories,
        IReadOnlyList<string> SupportedEntityRoutes);

    [Fact]
    public async Task OperationalEnums_MatchesCommittedContractSnapshot()
    {
        var repoRoot = FindRepoRoot();
        var snapshotPath = Path.Combine(repoRoot, "contracts", "operational-enums.json");

        var criterionCategories = Enum.GetValues<CriterionCategory>()
            .Select(c => c.ToSnakeCase())
            .ToArray();

        var supportedEntityRoutes = new[] { "posting", "company", "application" };

        var snapshotObj = new OperationalEnumsSnapshot(
            CriterionCategories: criterionCategories,
            SupportedEntityRoutes: supportedEntityRoutes);

        var generatedJson = JsonSerializer.Serialize(snapshotObj, IndentedJsonOptions) + Environment.NewLine;

        if (!File.Exists(snapshotPath))
        {
            await File.WriteAllTextAsync(snapshotPath, generatedJson);
            Assert.Fail($"Committed snapshot did not exist at '{snapshotPath}'. Created it now; commit it to source control.");
        }

        var committedJson = await File.ReadAllTextAsync(snapshotPath);

        var normalizedCommitted = committedJson.Replace("\r\n", "\n").Trim();
        var normalizedGenerated = generatedJson.Replace("\r\n", "\n").Trim();

        if (normalizedCommitted != normalizedGenerated)
        {
            await File.WriteAllTextAsync(snapshotPath, generatedJson);
            Assert.Fail(
                $"Committed operational enums snapshot at '{snapshotPath}' differs from Domain CriterionCategory enums. " +
                "The file has been updated with generated content; review and commit it.");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate RoleRadar.sln from test base directory.");
    }
}
