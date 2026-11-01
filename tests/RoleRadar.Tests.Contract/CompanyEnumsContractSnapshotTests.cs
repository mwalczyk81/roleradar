using System.Text.Json;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class CompanyEnumsContractSnapshotTests
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record CompanyEnumsSnapshot(
        IReadOnlyList<string> Statuses,
        IReadOnlyList<string> OwnershipTypes);

    [Fact]
    public async Task CompanyEnums_MatchesCommittedContractSnapshot()
    {
        var repoRoot = FindRepoRoot();
        var snapshotPath = Path.Combine(repoRoot, "contracts", "company-enums.json");

        var statuses = Enum.GetValues<CompanyStatus>()
            .Select(s => s.ToSnakeCase())
            .ToArray();

        var ownershipTypes = Enum.GetValues<OwnershipType>()
            .Select(o => o.ToSnakeCase())
            .ToArray();

        var snapshotObj = new CompanyEnumsSnapshot(
            Statuses: statuses,
            OwnershipTypes: ownershipTypes);

        var generatedJson = JsonSerializer.Serialize(snapshotObj, IndentedJsonOptions) + Environment.NewLine;

        if (!File.Exists(snapshotPath))
        {
            await File.WriteAllTextAsync(snapshotPath, generatedJson);
            Assert.Fail($"Committed snapshot did not exist at '{snapshotPath}'. Created it now; commit it to source control.");
        }

        var committedJson = await File.ReadAllTextAsync(snapshotPath);

        // Normalize line endings for cross-platform stability
        var normalizedCommitted = committedJson.Replace("\r\n", "\n").Trim();
        var normalizedGenerated = generatedJson.Replace("\r\n", "\n").Trim();

        if (normalizedCommitted != normalizedGenerated)
        {
            await File.WriteAllTextAsync(snapshotPath, generatedJson);
            Assert.Fail(
                $"Committed company enums snapshot at '{snapshotPath}' differs from Domain CompanyStatus/OwnershipType enums. " +
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
