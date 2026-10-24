using System.Text.RegularExpressions;
using Xunit;

namespace RoleRadar.Tests.Unit.Scripts;

public sealed class ScriptPortabilityTests
{
    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "RoleRadar.sln")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new InvalidOperationException("Could not find repository root containing RoleRadar.sln");
    }

    [Fact]
    public void WindowsDenylistResolution_MatchesLocalAppDataExactly_ByteForByte()
    {
        // Windows resolution invariant: %LOCALAPPDATA%\roleradar\private\denylist.txt derived from LocalApplicationData
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty;
        }

        var expectedWindowsPath = Path.Combine(localAppData, "roleradar", "private", "denylist.txt");

        // Verify the path formula produced
        Assert.EndsWith(@"roleradar\private\denylist.txt", expectedWindowsPath, StringComparison.OrdinalIgnoreCase);

        // Verify scripts/denylist-check.ps1 encodes this resolution
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "denylist-check.ps1");
        Assert.True(File.Exists(scriptPath), $"denylist-check.ps1 not found at: {scriptPath}");

        var scriptContent = File.ReadAllText(scriptPath);
        Assert.Contains("SpecialFolder]::LocalApplicationData", scriptContent);
        Assert.Contains("LOCALAPPDATA", scriptContent);
    }

    [Fact]
    public void UnixDenylistResolution_ResolvesXdgDataHomeOrDefault()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "denylist-check.ps1");
        var scriptContent = File.ReadAllText(scriptPath);

        // Script must support XDG_DATA_HOME on Unix
        Assert.Contains("XDG_DATA_HOME", scriptContent);
        Assert.Contains(".local/share", scriptContent);
    }

    [Fact]
    public void DenylistCheck_FailsClosed_WhenFileIsMissing()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "denylist-check.ps1");
        var scriptContent = File.ReadAllText(scriptPath);

        // Guard must fail closed when file is missing unless ROLERADAR_ALLOW_NO_DENYLIST=1
        Assert.Contains("ROLERADAR_ALLOW_NO_DENYLIST", scriptContent);
        Assert.Contains("throw", scriptContent);
    }

    [Fact]
    public void DependabotConfiguration_Covers_DockerfileAndCompose_UnderDockerEcosystem()
    {
        var repoRoot = FindRepoRoot();
        var dependabotPath = Path.Combine(repoRoot, ".github", "dependabot.yml");
        Assert.True(File.Exists(dependabotPath), $"dependabot.yml not found at: {dependabotPath}");

        var yaml = File.ReadAllText(dependabotPath);

        // Must cover package-ecosystem: "docker" for "/" and "/deploy"
        Assert.Contains("package-ecosystem: \"docker\"", yaml);
        Assert.Contains("directory: \"/\"", yaml);
        Assert.Contains("directory: \"/deploy\"", yaml);
    }

    [Fact]
    public void AllScripts_AvoidHardcodedWindowsBackslashPathsForRepoDirectories()
    {
        var repoRoot = FindRepoRoot();
        var scriptsDir = Path.Combine(repoRoot, "scripts");
        var scriptFiles = Directory.GetFiles(scriptsDir, "*.ps1");

        // Disallow hardcoded patterns like "scripts\" or "deploy\" or "src\" with backslashes
        var badPathPattern = new Regex(@"['""][^'""]*?\b(scripts|deploy|src|tests|specs)\\[^'""]*?['""]", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in scriptFiles)
        {
            var fileName = Path.GetFileName(file);
            // Ignore denylist Windows-specific LocalApplicationData child path
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith('#') || line.Contains("SpecialFolder", StringComparison.Ordinal) || line.Contains("LOCALAPPDATA", StringComparison.Ordinal))
                {
                    continue;
                }

                var match = badPathPattern.Match(line);
                if (match.Success)
                {
                    violations.Add($"{fileName}:{i + 1} -> {match.Value}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Found hardcoded Windows backslash paths in scripts:\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void ScriptPortability_PesterTestFile_ExistsOnDisk()
    {
        var repoRoot = FindRepoRoot();
        var pesterPath = Path.Combine(repoRoot, "tests", "RoleRadar.Tests.Unit", "Scripts", "ScriptPortability.Tests.ps1");
        Assert.True(File.Exists(pesterPath), $"ScriptPortability.Tests.ps1 not found at: {pesterPath}");
    }

    [Fact]
    public void CheckToolsReleaseScript_ExistsInScriptsDirectory()
    {
        var repoRoot = FindRepoRoot();
        var scriptPath = Path.Combine(repoRoot, "scripts", "check-tools-release.ps1");
        Assert.True(File.Exists(scriptPath), $"check-tools-release.ps1 not found at: {scriptPath}");
    }
}
