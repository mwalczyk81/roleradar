using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using Xunit;

namespace RoleRadar.Tests.Architecture;

public sealed class LayerDependencyArchitectureTests
{
    private static readonly Assembly ApiAssembly = typeof(RoleRadar.Api.Program).Assembly;
    private static readonly Assembly IdentityAssembly = typeof(RoleRadar.Identity.Program).Assembly;

    [Fact]
    public void Only_RoleRadarIdentity_References_DuendePackages()
    {
        var repoRoot = LocateRepositoryRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcDir), $"Expected src directory at {srcDir}");

        var csprojFiles = Directory.EnumerateFiles(srcDir, "*.csproj", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(csprojFiles);

        var violatingProjects = new List<string>();

        foreach (var csproj in csprojFiles)
        {
            var projectName = Path.GetFileNameWithoutExtension(csproj);
            if (string.Equals(projectName, "RoleRadar.Identity", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var doc = XDocument.Load(csproj);
            var packageRefs = doc.Descendants("PackageReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToArray();

            foreach (var packageRef in packageRefs)
            {
                if (packageRef.StartsWith("Duende.", StringComparison.OrdinalIgnoreCase))
                {
                    violatingProjects.Add($"{projectName} references package {packageRef}");
                }
            }

            var projectRefs = doc.Descendants("ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToArray();

            foreach (var projectRef in projectRefs)
            {
                if (string.Equals(projectName, "RoleRadar.Api", StringComparison.OrdinalIgnoreCase) &&
                    projectRef.Contains("RoleRadar.Identity", StringComparison.OrdinalIgnoreCase))
                {
                    violatingProjects.Add($"{projectName} references project {projectRef}");
                }
            }
        }

        Assert.True(
            violatingProjects.Count == 0,
            $"Only RoleRadar.Identity may reference Duende packages or projects. Violations:\n{string.Join("\n", violatingProjects)}");
    }

    [Fact]
    public void RoleRadarApi_MustNotReference_Duende()
    {
        var netArchResult = Types.InAssembly(ApiAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Duende",
                "Duende.IdentityServer",
                "Duende.IdentityModel",
                "RoleRadar.Identity")
            .GetResult();

        Assert.True(
            netArchResult.IsSuccessful,
            $"RoleRadar.Api must not depend on Duende or RoleRadar.Identity: {string.Join(", ", netArchResult.FailingTypeNames ?? [])}");

        var referencedAssemblies = ApiAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(
            referencedAssemblies,
            name => name.StartsWith("Duende", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("RoleRadar.Identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RoleRadarApi_Types_MustNotDependOn_MongoDriver()
    {
        // 1. NetArchTest assembly dependency check: no types in RoleRadar.Api may depend on Mongo driver/BSON
        var result = Types.InAssembly(ApiAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "MongoDB.Driver",
                "MongoDB.Bson")
            .GetResult();

        Assert.True(result.IsSuccessful, $"RoleRadar.Api inward dependency violation: {string.Join(", ", result.FailingTypeNames ?? [])}");

        // 2. csproj file check: no direct package references to MongoDB driver
        var repoRoot = LocateRepositoryRoot();
        var apiCsproj = Path.Combine(repoRoot, "src", "RoleRadar.Api", "RoleRadar.Api.csproj");
        Assert.True(File.Exists(apiCsproj), $"Expected csproj at {apiCsproj}");

        var doc = XDocument.Load(apiCsproj);
        var directDependencies = doc.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToArray();

        Assert.DoesNotContain(directDependencies, d => d.Contains("MongoDB", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OwnerId_StringLiterals_MustNotAppearOutsideOwnerIdClass()
    {
        var repoRoot = LocateRepositoryRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcDir), $"Expected src directory at {srcDir}");

        var offendingFiles = new List<string>();
        var forbiddenLiterals = new[] { "default-owner", "local-owner" };

        var csFiles = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        foreach (var file in csFiles)
        {
            var relativePath = Path.GetRelativePath(srcDir, file).Replace('\\', '/');
            if (relativePath.Equals("RoleRadar.Domain/Common/OwnerId.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var literal in forbiddenLiterals)
                {
                    if (line.Contains(literal, StringComparison.OrdinalIgnoreCase))
                    {
                        offendingFiles.Add($"{relativePath}:{i + 1} contains '{literal}'");
                    }
                }
            }
        }

        Assert.True(
            offendingFiles.Count == 0,
            $"Hardcoded owner string literals must not appear outside Domain/Common/OwnerId.cs. Violations:\n{string.Join("\n", offendingFiles)}");
    }

    private static string LocateRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing RoleRadar.sln and src directory.");
    }
}
