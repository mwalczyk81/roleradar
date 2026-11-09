using System.Text.RegularExpressions;

namespace RoleRadar.Tests;

/// <summary>
/// The MongoDB image every Testcontainers fixture starts. It is read from <c>deploy/docker-compose.yml</c> so the tests
/// always run on the same server version as the stack, and a Dependabot bump of the compose file moves both
/// (ADR 0040). Linked into the Contract project, so there is one copy of this file.
/// </summary>
internal static partial class MongoTestImage
{
    public static string Tag { get; } = Read();

    private static string Read()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException("RoleRadar.sln was not found above the test output folder.");
        }

        var compose = File.ReadAllText(Path.Combine(dir.FullName, "deploy", "docker-compose.yml"));
        var match = ImageLine().Match(compose);
        if (!match.Success)
        {
            throw new InvalidOperationException("No 'image: mongo:<tag>' line found in deploy/docker-compose.yml.");
        }

        return match.Groups[1].Value;
    }

    [GeneratedRegex(@"^\s*image:\s*(mongo:\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex ImageLine();
}
