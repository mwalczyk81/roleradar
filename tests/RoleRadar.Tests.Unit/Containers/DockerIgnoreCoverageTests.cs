using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace RoleRadar.Tests.Unit.Containers;

/// <summary>
/// The repository's <c>.dockerignore</c> is deny-by-default (<c>**</c> followed by allow-list entries).
/// A source file type that is not on the allow-list is silently dropped from the build context, which
/// is exactly how the Identity image once shipped without its compiled Razor login page.
/// These tests evaluate the real <c>.dockerignore</c> against the real files under <c>src/</c> so that
/// any future change that drops a build input (for example removing <c>!src/**/*.cshtml</c>) fails fast,
/// without needing a Docker daemon.
/// </summary>
public sealed class DockerIgnoreCoverageTests
{
    // Extensions that are compile inputs for the shipped images. If one is excluded from the
    // build context, the image either fails to build or (worse for Razor) builds without the UI.
    private static readonly string[] BuildInputExtensions = [".cs", ".csproj", ".cshtml", ".razor"];

    [Fact]
    public void DockerIgnore_AllowsEveryBuildInputUnderSrc()
    {
        // Covers: every compile input under src/ (including Razor pages) survives the deny-by-default
        // rules, so the Identity login page can never be dropped from the Docker build context again.
        var repoRoot = FindRepoRoot();
        var rules = ParseRules(File.ReadAllLines(Path.Combine(repoRoot, ".dockerignore")));
        var srcRoot = Path.Combine(repoRoot, "src");

        var excluded = Directory
            .EnumerateFiles(srcRoot, "*.*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .Where(rel => !rel.Contains("/bin/", StringComparison.Ordinal) && !rel.Contains("/obj/", StringComparison.Ordinal))
            .Where(rel => BuildInputExtensions.Contains(Path.GetExtension(rel), StringComparer.OrdinalIgnoreCase))
            .Where(rel => !IsIncluded(rules, rel))
            .ToList();

        Assert.True(
            excluded.Count == 0,
            $".dockerignore excludes build inputs from the Docker context: {string.Join(", ", excluded)}");
    }

    [Fact]
    public void DockerIgnore_AllowsTheIdentityLoginPageSpecifically()
    {
        // Covers: a pinpoint guard for the regression, so a failure message names the page directly
        // and the test also proves the file exists (a renamed page cannot make the suite vacuous).
        var repoRoot = FindRepoRoot();
        const string loginPage = "src/RoleRadar.Identity/Pages/Account/Login.cshtml";
        Assert.True(File.Exists(Path.Combine(repoRoot, loginPage)), $"Expected '{loginPage}' to exist.");

        var rules = ParseRules(File.ReadAllLines(Path.Combine(repoRoot, ".dockerignore")));

        Assert.True(IsIncluded(rules, loginPage), $".dockerignore must allow '{loginPage}'.");
    }

    [Fact]
    public void Matcher_WithoutCshtmlAllowEntry_ExcludesLoginPage()
    {
        // Covers: the matcher itself. Using the real file with the allow entry removed must report the
        // page as excluded; otherwise the two tests above could never fail and would give false comfort.
        var repoRoot = FindRepoRoot();
        var lines = File.ReadAllLines(Path.Combine(repoRoot, ".dockerignore"))
            .Where(l => !l.Trim().Equals("!src/**/*.cshtml", StringComparison.Ordinal))
            .ToArray();
        var rules = ParseRules(lines);

        Assert.False(IsIncluded(rules, "src/RoleRadar.Identity/Pages/Account/Login.cshtml"));
        Assert.True(IsIncluded(rules, "src/RoleRadar.Identity/Program.cs"));
    }

    private sealed record Rule(bool Negated, Regex Pattern);

    private static List<Rule> ParseRules(IEnumerable<string> lines)
    {
        var rules = new List<Rule>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var negated = line.StartsWith('!');
            var glob = (negated ? line[1..] : line).TrimEnd('/');
            rules.Add(new Rule(negated, new Regex("^" + GlobToRegex(glob) + "$", RegexOptions.CultureInvariant)));
        }

        return rules;
    }

    // Docker semantics: the LAST matching rule wins; a rule that matches a parent directory also
    // applies to the files beneath it; a path matched by no rule is included.
    private static bool IsIncluded(IReadOnlyList<Rule> rules, string relativePath)
    {
        var segments = relativePath.Split('/');
        var candidates = Enumerable.Range(1, segments.Length)
            .Select(count => string.Join('/', segments.Take(count)))
            .ToArray();

        var included = true;
        foreach (var rule in rules)
        {
            if (candidates.Any(candidate => rule.Pattern.IsMatch(candidate)))
            {
                included = rule.Negated;
            }
        }

        return included;
    }

    private static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                var followedBySlash = i + 2 < glob.Length && glob[i + 2] == '/';
                sb.Append(followedBySlash ? "(?:.*/)?" : ".*");
                i += followedBySlash ? 2 : 1;
            }
            else if (c == '*')
            {
                sb.Append("[^/]*");
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }

        return sb.ToString();
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
