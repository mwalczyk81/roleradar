using System.Reflection;
using System.Text.RegularExpressions;
using RoleRadar.Tests.Integration.Hosting;
using Xunit;

namespace RoleRadar.Tests.Integration;

/// <summary>
/// Test lanes (feature 010, contracts/test-lanes.md, ADR 0036). Heavy suites carry a <c>[Trait("Lane", "...")]</c> and
/// the scripts and workflows select them by that trait, never by class-name fragments, so a new test class cannot be
/// silently left out of the image, web-container, scan or real-stack runs. Reflection and file reads only; no Docker.
/// </summary>
[Trait("Lane", "default")]
public sealed partial class TestLaneCoverageTests
{
    private static readonly string[] KnownLanes = ["images", "web-container", "real-stack", "scan", "default"];

    /// <summary>Name fragments the old scripts used to pick these suites; selecting by them is what let classes drift out.</summary>
    private static readonly string[] BannedFragments =
        ["WebContainer", "RealStack", "Hosting", "ShippedHost", "StartupSmoke", "ContactNoLeak", "CliComposition"];

    /// <summary>
    /// What it means to use the shipped images or a launched host: running or resolving an image through the harness,
    /// starting the MCP or CLI host (which run in a container under ROLERADAR_TEST_IMAGE=1), or building or running a
    /// container directly. A static helper such as <c>ContainerTestHarness.ValidateDatabaseNameForDrop</c>, or a
    /// <c>docker exec</c> into a test Mongo, does not count: that is a host-process test.
    /// </summary>
    [GeneratedRegex(@"ContainerTestHarness\.(Run|Ensure|Resolve)\w*|McpProcessClient\.StartAsync|LaunchedCliRunner\.\w+|docker\s+(run|build|create)\b|ROLERADAR_\w*IMAGE")]
    private static partial Regex ImageMachineryUse();

    private static bool UsesTheImages(string sourceText, string ns) =>
        ns.EndsWith(".Hosting", StringComparison.Ordinal)
        || ns.EndsWith(".Containers", StringComparison.Ordinal)
        || ImageMachineryUse().IsMatch(sourceText);

    private static string Root => ContainerTestHarness.FindRepoRoot();

    private static Assembly TestAssembly => typeof(TestLaneCoverageTests).Assembly;

    private static IEnumerable<string> ScriptAndWorkflowFiles()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "scripts"), "*.ps1"))
        {
            yield return file;
        }

        foreach (var file in Directory.GetFiles(Path.Combine(Root, ".github", "workflows"), "*.yml"))
        {
            yield return file;
        }
    }

    private static bool IsTestMethod(MethodInfo method) =>
        method.GetCustomAttributesData().Any(a => a.AttributeType.Name is "FactAttribute" or "TheoryAttribute");

    private static IReadOnlyList<Type> TestClasses() =>
        [.. TestAssembly.GetTypes().Where(t => t.IsClass && t.GetMethods().Any(IsTestMethod))];

    private static string[] LaneValues(IEnumerable<CustomAttributeData> attributes) =>
        [.. attributes
            .Where(a => a.AttributeType.Name == "TraitAttribute" && a.ConstructorArguments.Count == 2 && (string?)a.ConstructorArguments[0].Value == "Lane")
            .Select(a => (string)a.ConstructorArguments[1].Value!)];

    private static string[] ClassLanes(Type type) => LaneValues(type.GetCustomAttributesData());

    private static string[] MethodLanes(MethodInfo method) => LaneValues(method.GetCustomAttributesData());

    [GeneratedRegex(@"Lane!?=([A-Za-z][A-Za-z-]*)")]
    private static partial Regex LaneReference();

    [GeneratedRegex(@"FullyQualifiedName[~!=]+~?([A-Za-z]+)")]
    private static partial Regex NameFragment();

    [Fact]
    public void EveryLaneValue_IsAKnownLane_OnClassesAndMethods()
    {
        var unknown = new List<string>();
        foreach (var type in TestClasses())
        {
            unknown.AddRange(ClassLanes(type).Where(l => !KnownLanes.Contains(l)).Select(l => $"{type.FullName}: '{l}'"));
            foreach (var method in type.GetMethods().Where(IsTestMethod))
            {
                unknown.AddRange(MethodLanes(method).Where(l => !KnownLanes.Contains(l)).Select(l => $"{type.FullName}.{method.Name}: '{l}'"));
            }
        }

        Assert.True(unknown.Count == 0, "Unknown Lane trait values (known: " + string.Join(", ", KnownLanes) + "): " + string.Join("; ", unknown));
    }

    [Fact]
    public void EveryKnownLane_HasAtLeastOneTestClass_SoALaneNeverRunsZeroTests()
    {
        var empty = KnownLanes.Where(lane => !TestClasses().Any(t => ClassLanes(t).Contains(lane))).ToArray();

        Assert.True(empty.Length == 0, "Lanes with no test class (a run selecting them would execute zero tests): " + string.Join(", ", empty));
    }

    [Fact]
    public void EveryLaneNamedInAScriptOrWorkflow_IsKnown_AndHasTestClasses()
    {
        var problems = new List<string>();
        foreach (var file in ScriptAndWorkflowFiles())
        {
            foreach (Match match in LaneReference().Matches(File.ReadAllText(file)))
            {
                var lane = match.Groups[1].Value;
                var name = Path.GetRelativePath(Root, file).Replace('\\', '/');
                if (!KnownLanes.Contains(lane))
                {
                    problems.Add($"{name} selects unknown lane '{lane}'");
                }
                else if (!TestClasses().Any(t => ClassLanes(t).Contains(lane)))
                {
                    problems.Add($"{name} selects lane '{lane}' which has no test class");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void EveryClassUsingTheImageMachinery_OrLivingInHostingOrContainers_HasALane()
    {
        var testDir = Path.Combine(Root, "tests", "RoleRadar.Tests.Integration");
        var unclassified = new List<string>();

        foreach (var file in Directory.GetFiles(testDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("[Fact", StringComparison.Ordinal) && !text.Contains("[Theory", StringComparison.Ordinal))
            {
                continue;
            }

            var ns = Regex.Match(text, @"^namespace\s+([\w.]+)", RegexOptions.Multiline).Groups[1].Value;
            if (!UsesTheImages(text, ns))
            {
                continue;
            }

            foreach (Match cls in Regex.Matches(text, @"(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+(\w+)"))
            {
                var type = TestAssembly.GetType($"{ns}.{cls.Groups[1].Value}");
                if (type is null || !type.GetMethods().Any(IsTestMethod))
                {
                    continue;
                }

                if (ClassLanes(type).Length == 0)
                {
                    unclassified.Add(type.FullName!);
                }
            }
        }

        Assert.True(
            unclassified.Count == 0,
            "Test classes that use the container or launched-host machinery (or live in Hosting or Containers) but carry no Lane trait: "
            + string.Join(", ", unclassified.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// The images lane is run a second time in image mode against freshly built images. A class that never uses the
    /// images (a host-process test against a Testcontainers Mongo, a static guard) gains nothing there and only adds
    /// time and flakes, so it must not be in the lane (found in feature 010: the two Backup classes).
    /// </summary>
    [Fact]
    public void EveryImagesLaneClass_ReallyUsesTheImages()
    {
        var testDir = Path.Combine(Root, "tests", "RoleRadar.Tests.Integration");
        var idle = new List<string>();

        foreach (var file in Directory.GetFiles(testDir, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")))
        {
            var text = File.ReadAllText(file);
            var ns = Regex.Match(text, @"^namespace\s+([\w.]+)", RegexOptions.Multiline).Groups[1].Value;
            foreach (Match cls in Regex.Matches(text, @"(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+(\w+)"))
            {
                var type = TestAssembly.GetType($"{ns}.{cls.Groups[1].Value}");
                if (type is not null && ClassLanes(type).Contains("images") && !UsesTheImages(text, ns))
                {
                    idle.Add(type.FullName!);
                }
            }
        }

        Assert.True(
            idle.Count == 0,
            "Classes in the images lane that never use the shipped images or a launched host (leave them untagged or mark them default): "
            + string.Join(", ", idle.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void NoTestMethod_CarriesADifferentLaneThanItsClass()
    {
        var mixed = TestClasses()
            .SelectMany(t => t.GetMethods().Where(IsTestMethod).Select(m => (Type: t, Method: m)))
            .Where(x => MethodLanes(x.Method).Any(l => !ClassLanes(x.Type).Contains(l)))
            .Select(x => $"{x.Type.FullName}.{x.Method.Name}")
            .ToArray();

        Assert.True(mixed.Length == 0, "A method-level Lane that differs from its class would put the test in two lanes: " + string.Join(", ", mixed));
    }

    [Fact]
    public void NoScriptOrWorkflow_SelectsTheseSuites_ByClassNameFragment()
    {
        var offenders = new List<string>();
        foreach (var file in ScriptAndWorkflowFiles())
        {
            var name = Path.GetRelativePath(Root, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in NameFragment().Matches(lines[i]))
                {
                    var fragment = match.Groups[1].Value;
                    if (BannedFragments.Any(b => fragment.Contains(b, StringComparison.Ordinal)))
                    {
                        offenders.Add($"{name}:{i + 1} selects by name fragment '{fragment}'");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0, "Select by Lane trait instead: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// A test that writes the scan report it then checks can only pass. The real reports come from Trivy; the only
    /// tests allowed to write one are the evaluator's own, which write into a temporary folder.
    /// </summary>
    [Fact]
    public void NoTestWritesAScanReport_OnlyTheRealScanDoes()
    {
        var testDir = Path.Combine(Root, "tests", "RoleRadar.Tests.Integration");
        var offenders = Directory.GetFiles(testDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Path.GetFileName(f) is not ("TrivyReportEvaluatorTests.cs" or "TestLaneCoverageTests.cs"))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("trivy-report", StringComparison.OrdinalIgnoreCase)
                    && (text.Contains("WriteAllText", StringComparison.Ordinal) || text.Contains("File.Create", StringComparison.Ordinal));
            })
            .Select(f => Path.GetRelativePath(testDir, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(offenders.Length == 0, "These test files write a trivy-report file (a test must never produce the report it checks): " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryNameFragmentAimedAtTheIntegrationProject_MatchesAtLeastOneTestClass()
    {
        var classNames = TestClasses().Select(t => t.FullName!).ToArray();
        var zero = new List<string>();
        foreach (var file in ScriptAndWorkflowFiles())
        {
            var name = Path.GetRelativePath(Root, file).Replace('\\', '/');
            foreach (var line in File.ReadAllLines(file).Where(l => l.Contains("RoleRadar.Tests.Integration", StringComparison.Ordinal)))
            {
                foreach (Match match in NameFragment().Matches(line))
                {
                    var fragment = match.Groups[1].Value;
                    if (!classNames.Any(c => c.Contains(fragment, StringComparison.Ordinal)))
                    {
                        zero.Add($"{name}: '{fragment}'");
                    }
                }
            }
        }

        Assert.True(zero.Count == 0, "Name filters that select no Integration test class (they would run zero tests): " + string.Join("; ", zero));
    }
}
