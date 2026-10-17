using System.Text.RegularExpressions;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Infrastructure;

public sealed class MongoOptionsReadmeConsistencyTests
{
    [Fact]
    public void ReadmeEnvironmentVariableTable_DefaultConnectionString_MatchesMongoOptionsDefault()
    {
        var repoRoot = FindRepoRoot();
        var readmePath = Path.Combine(repoRoot, "README.md");
        Assert.True(File.Exists(readmePath), $"Expected README.md at '{readmePath}'.");

        var readmeContent = File.ReadAllText(readmePath);

        // Matches row: | `ROLERADAR_MONGO_CONNECTION_STRING` | `mongodb://...` | Description |
        var match = Regex.Match(
            readmeContent,
            @"\|\s*`ROLERADAR_MONGO_CONNECTION_STRING`\s*\|\s*`([^`]+)`\s*\|",
            RegexOptions.Multiline);

        Assert.True(match.Success, "Could not find ROLERADAR_MONGO_CONNECTION_STRING row in README.md table.");

        var parsedConnectionString = match.Groups[1].Value.Trim();
        var defaultOptions = new MongoOptions();

        Assert.Equal(defaultOptions.ConnectionString, parsedConnectionString);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
