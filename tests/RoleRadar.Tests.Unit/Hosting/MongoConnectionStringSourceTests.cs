using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Hosting;

/// <summary>
/// Covers the Docker-secrets style connection string source (FR-005, FR-011).
/// Passing a credentialed URI with <c>docker run -e</c> exposes it through <c>docker inspect</c>;
/// <c>ROLERADAR_MONGO_CONNECTION_STRING_FILE</c> lets the URI live in a read-only mounted file instead.
/// </summary>
public sealed class MongoConnectionStringSourceTests
{
    private const string PlainVar = MongoConnectionStringSource.ConnectionStringVariable;
    private const string FileVar = MongoConnectionStringSource.ConnectionStringFileVariable;

    [Fact]
    public void Resolve_ReturnsNull_WhenNeitherVariableIsSet()
    {
        var result = MongoConnectionStringSource.Resolve(_ => null);

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_ReturnsPlainVariable_WhenOnlyPlainVariableIsSet()
    {
        var env = new Dictionary<string, string?> { [PlainVar] = "  mongodb://127.0.0.1:27017  " };

        var result = MongoConnectionStringSource.Resolve(k => env.GetValueOrDefault(k));

        Assert.Equal("mongodb://127.0.0.1:27017", result);
    }

    [Fact]
    public void Resolve_ReadsAndTrimsFile_WhenFileVariableIsSet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rr-uri-{Guid.NewGuid():N}");
        File.WriteAllText(path, "mongodb://u:FilePw123@db.example:27017/?tls=true\n");
        try
        {
            var env = new Dictionary<string, string?> { [FileVar] = path };

            var result = MongoConnectionStringSource.Resolve(k => env.GetValueOrDefault(k));

            Assert.Equal("mongodb://u:FilePw123@db.example:27017/?tls=true", result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Resolve_Throws_WhenBothVariablesAreSet()
    {
        // Ambiguous configuration is refused rather than silently preferring one source.
        var env = new Dictionary<string, string?> { [PlainVar] = "mongodb://a", [FileVar] = "/run/secrets/x" };

        var ex = Assert.Throws<InvalidOperationException>(() => MongoConnectionStringSource.Resolve(k => env.GetValueOrDefault(k)));

        Assert.Contains(FileVar, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_Throws_WithoutLeakingContent_WhenFileIsMissing()
    {
        var env = new Dictionary<string, string?> { [FileVar] = "/definitely/missing/rr-uri" };

        var ex = Assert.Throws<InvalidOperationException>(() => MongoConnectionStringSource.Resolve(k => env.GetValueOrDefault(k)));

        Assert.Contains(FileVar, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_Throws_WhenFileIsEmpty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rr-uri-{Guid.NewGuid():N}");
        File.WriteAllText(path, "   \n");
        try
        {
            var env = new Dictionary<string, string?> { [FileVar] = path };

            Assert.Throws<InvalidOperationException>(() => MongoConnectionStringSource.Resolve(k => env.GetValueOrDefault(k)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
