using System.Text.Json;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class DispositionMatrixContractSnapshotTests
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed record TransitionRuleSnapshot(
        string From,
        string To,
        string? RequiredPolicy,
        bool RequiresReason);

    // Only the transition table is pinned: it is behaviour shared by Domain and web. The disposition and resurface
    // policy values are generated into the OpenAPI schema (ADR 0035), so they are no longer snapshotted here.
    private sealed record DispositionMatrixSnapshot(
        IReadOnlyList<TransitionRuleSnapshot> Transitions);

    [Fact]
    public async Task DispositionMatrix_MatchesCommittedContractSnapshot()
    {
        var repoRoot = FindRepoRoot();
        var snapshotPath = Path.Combine(repoRoot, "contracts", "disposition-matrix.json");

        var transitions = new List<TransitionRuleSnapshot>();

        foreach (var from in Enum.GetValues<PostingDisposition>())
        {
            foreach (var to in Enum.GetValues<PostingDisposition>())
            {
                if (to == PostingDisposition.Applied)
                {
                    continue;
                }

                var canNever = DispositionMatrix.CanTransition(from, to, ResurfacePolicy.Never);
                var canReopened = DispositionMatrix.CanTransition(from, to, ResurfacePolicy.IfReopened);

                if (canNever)
                {
                    transitions.Add(new TransitionRuleSnapshot(
                        From: from.ToSnakeCase(),
                        To: to.ToSnakeCase(),
                        RequiredPolicy: null,
                        RequiresReason: from == PostingDisposition.Rejected && to == PostingDisposition.Lead));
                }
                else if (canReopened)
                {
                    transitions.Add(new TransitionRuleSnapshot(
                        From: from.ToSnakeCase(),
                        To: to.ToSnakeCase(),
                        RequiredPolicy: ResurfacePolicy.IfReopened.ToSnakeCase(),
                        RequiresReason: false));
                }
            }
        }

        var snapshotObj = new DispositionMatrixSnapshot(Transitions: transitions);

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
                $"Committed disposition matrix snapshot at '{snapshotPath}' differs from Domain DispositionMatrix rules. " +
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
