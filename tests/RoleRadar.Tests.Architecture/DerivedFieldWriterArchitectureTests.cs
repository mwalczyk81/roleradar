namespace RoleRadar.Tests.Architecture;

using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// 007 / ADR 0031: the derived read-model fields behind the Insights drill-downs are written only at the
/// repository choke point and by reconcile. A source scan (string literals are invisible to type-based
/// architecture rules) fails the build if any other file names them.
/// </summary>
public sealed partial class DerivedFieldWriterArchitectureTests
{
    private static readonly string[] DerivedFieldNames =
    [
        "has_application",
        "has_offer",
        "has_rejection",
        "rejected_before_interview"
    ];

    // Files allowed to name the fields. The read models expose them as plain properties and never use the
    // snake_case names, so they do not appear here.
    private static readonly string[] AllowedFiles =
    [
        "src/RoleRadar.Infrastructure/Mongo/TrackingBsonFieldNames.cs",
        "src/RoleRadar.Infrastructure/Mongo/Repositories/MongoApplicationRepository.cs",
        "src/RoleRadar.Infrastructure/Mongo/Repositories/MongoPostingRepository.cs",
        "src/RoleRadar.Infrastructure/Mongo/Reconcile/ReadModelReconciliationService.cs",
        "src/RoleRadar.Infrastructure/Mongo/Reading/ReadModelClassMaps.cs",
        "src/RoleRadar.Infrastructure/Mongo/TrackingIndexInitializer.cs"
    ];

    private static readonly string[] RequiredWriterFiles =
    [
        "src/RoleRadar.Infrastructure/Mongo/Repositories/MongoApplicationRepository.cs",
        "src/RoleRadar.Infrastructure/Mongo/Repositories/MongoPostingRepository.cs",
        "src/RoleRadar.Infrastructure/Mongo/Reconcile/ReadModelReconciliationService.cs"
    ];

    [Fact]
    public void DerivedFields_AreNamedOnlyInTheAllowlistedFiles()
    {
        var root = LocateRepositoryRoot();
        var offenders = new List<string>();

        foreach (var (relative, text) in SourceFiles(root))
        {
            if (AllowedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var hit = FieldReference().Match(text);
            if (hit.Success)
            {
                offenders.Add($"{relative} references '{hit.Value}'");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Derived Insights fields may only be written at the repository choke point, reconcile, the class map and the index initializer:\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void DerivedFields_AreActuallyWrittenAtTheChokePointAndByReconcile()
    {
        // Guards against the scan above passing vacuously (for example after a rename).
        var root = LocateRepositoryRoot();
        var files = SourceFiles(root).ToDictionary(f => f.Relative, f => f.Text, StringComparer.OrdinalIgnoreCase);

        foreach (var required in RequiredWriterFiles)
        {
            Assert.True(files.TryGetValue(required, out var text), $"Expected source file {required}");
            Assert.True(
                FieldReference().IsMatch(text!),
                $"{required} must write the derived Insights fields (ADR 0031) but does not reference them.");
        }
    }

    private static IEnumerable<(string Relative, string Text)> SourceFiles(string root)
    {
        var srcRoot = Path.Combine(root, "src");
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (relative, File.ReadAllText(file));
        }
    }

    // Quoted snake_case names, or the TrackingBsonFieldNames constants for them.
    [GeneratedRegex("\"(has_application|has_offer|has_rejection|rejected_before_interview)\"|TrackingBsonFieldNames\\.\\w+\\.(HasApplication|HasOffer|HasRejection|RejectedBeforeInterview)\\b")]
    private static partial Regex FieldReference();

    private static string LocateRepositoryRoot()
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
