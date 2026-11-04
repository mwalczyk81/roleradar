namespace RoleRadar.Tests.Architecture;

using System.Reflection;
using System.Xml.Linq;
using NetArchTest.Rules;
using Xunit;

public sealed class ODataSeamArchitectureTests
{
    private static readonly Assembly ApiAssembly = typeof(RoleRadar.Api.Program).Assembly;

    [Fact]
    public void RoleRadarApi_Contains_Zero_References_To_MongoDB_Driver()
    {
        var result = Types.InAssembly(ApiAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("MongoDB.Driver", "MongoDB.Bson")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"RoleRadar.Api violates Architecture rules by referencing MongoDB types directly: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact]
    public void RoleRadarApi_ProjectFile_Does_Not_Reference_MongoDB_Packages()
    {
        var repoRoot = LocateRepositoryRoot();
        var apiCsproj = Path.Combine(repoRoot, "src", "RoleRadar.Api", "RoleRadar.Api.csproj");
        Assert.True(File.Exists(apiCsproj), $"Expected project file at {apiCsproj}");

        var doc = XDocument.Load(apiCsproj);
        var packageRefs = doc.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToArray();

        foreach (var packageRef in packageRefs)
        {
            Assert.False(
                packageRef.StartsWith("MongoDB.", StringComparison.OrdinalIgnoreCase),
                $"RoleRadar.Api.csproj must not reference package '{packageRef}'. Reads must flow strictly through IOwnerReadSeam.");
        }
    }

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
