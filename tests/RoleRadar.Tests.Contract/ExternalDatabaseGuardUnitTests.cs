using MongoDB.Driver;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class ExternalDatabaseGuardUnitTests
{
    [Fact]
    public void OptIn_MissingOrDisabled_ThrowsOptInMissingException()
    {
        Assert.Throws<OptInMissingException>(() => ExternalMongoSafetyGuard.ValidateOptIn(null));
        Assert.Throws<OptInMissingException>(() => ExternalMongoSafetyGuard.ValidateOptIn("0"));
        Assert.Throws<OptInMissingException>(() => ExternalMongoSafetyGuard.ValidateOptIn("false"));
        Assert.Throws<OptInMissingException>(() => ExternalMongoSafetyGuard.ValidateConnectionString("mongodb://localhost:27017", isOptInEnabled: false));
    }

    [Fact]
    public void Case01_SrvImpliesTls_Allowed()
    {
        const string uri = "mongodb+srv://cluster0.example.mongodb.net/";
        var exception = Record.Exception(() => ExternalMongoSafetyGuard.ValidateConnectionString(uri, isOptInEnabled: true));
        Assert.Null(exception);
    }

    [Fact]
    public void Case02_SrvExplicitTlsDisabled_RejectedWithTlsRequiredException()
    {
        const string uri = "mongodb+srv://cluster0.example.mongodb.net/?tls=false";
        Assert.Throws<TlsRequiredException>(() => ExternalMongoSafetyGuard.ValidateConnectionString(uri, isOptInEnabled: true));
    }

    [Fact]
    public void Case03_DockerInternalTreatedAsNonLoopback_RejectedWithTlsRequiredException()
    {
        const string uri = "mongodb://host.docker.internal:27017/?tls=false";
        Assert.Throws<TlsRequiredException>(() => ExternalMongoSafetyGuard.ValidateConnectionString(uri, isOptInEnabled: true));
    }

    [Fact]
    public void Case04_Ipv4LoopbackSubnet127_AllowedWithoutTls()
    {
        const string uri = "mongodb://127.0.0.2:27017";
        var exception = Record.Exception(() => ExternalMongoSafetyGuard.ValidateConnectionString(uri, isOptInEnabled: true));
        Assert.Null(exception);
    }

    [Fact]
    public void Case05_Ipv6Loopback_AllowedWithoutTls()
    {
        const string uri = "mongodb://[::1]:27017";
        var exception = Record.Exception(() => ExternalMongoSafetyGuard.ValidateConnectionString(uri, isOptInEnabled: true));
        Assert.Null(exception);
    }

    [Fact]
    public void Case06_LocalhostLoopback_AllowedWithoutTls()
    {
        const string uri = "mongodb://localhost:27017";
        var exception = Record.Exception(() => ExternalMongoSafetyGuard.ValidateConnectionString(uri, isOptInEnabled: true));
        Assert.Null(exception);
    }

    [Fact]
    public void Case07_ConflictingTlsParameters_RejectedWithConflictingTlsParametersException()
    {
        const string uri = "mongodb://remote-db.example.com:27017/?tls=false&ssl=true";
        Assert.Throws<ConflictingTlsParametersException>(() => ExternalMongoSafetyGuard.ValidateConnectionString(uri, isOptInEnabled: true));
    }

    [Fact]
    public void Case08_RemoteWithExplicitTls_Allowed()
    {
        const string uri = "mongodb://remote-db.example.com:27017/?tls=true";
        var exception = Record.Exception(() => ExternalMongoSafetyGuard.ValidateConnectionString(uri, isOptInEnabled: true));
        Assert.Null(exception);
    }

    [Fact]
    public void Case09_RemotePlaintextRefused_RejectedWithTlsRequiredException()
    {
        const string uri = "mongodb://remote-db.example.com:27017";
        Assert.Throws<TlsRequiredException>(() => ExternalMongoSafetyGuard.ValidateConnectionString(uri, isOptInEnabled: true));
    }

    [Fact]
    public void Case10_LacksListDatabasesPrivilege_ThrowsRedactedPermissionExceptionWithoutSecretLeak()
    {
        const string sensitiveHost = "secret-cluster.mongodb.net";
        const string sensitiveUser = "adminUser";
        const string sensitivePass = "P@ssword123!";
        var underlyingException = new MongoException(
            $"not authorized on admin to execute command {{ listDatabases: 1 }} user: {sensitiveUser} at {sensitiveHost}");

        var thrown = Assert.Throws<RedactedPermissionException>(() =>
            ExternalMongoSafetyGuard.HandleListDatabasesException(underlyingException));

        Assert.Equal(
            "External database guard failed: user lacks 'listDatabases' cluster privilege to verify database prefix safety. Provide an account with cluster monitor or listDatabases privileges, or use a dedicated test cluster.",
            thrown.Message);

        Assert.DoesNotContain(sensitiveHost, thrown.Message);
        Assert.DoesNotContain(sensitiveUser, thrown.Message);
        Assert.DoesNotContain(sensitivePass, thrown.Message);
    }

    [Fact]
    public void Case11_ForeignDatabasePresent_ThrowsForeignDatabaseDetectedException()
    {
        string[] databasesWithForeign = ["admin", "config", "local", "rrtest_12345", "production_db"];
        var thrown = Assert.Throws<ForeignDatabaseDetectedException>(() =>
            ExternalMongoSafetyGuard.ValidateExistingDatabases(databasesWithForeign));

        Assert.Contains("production_db", thrown.Message);

        // System and rrtest_ databases only should pass without exception
        string[] safeDatabases = ["admin", "config", "local", "rrtest_018f2a1b9c3d70008000abcdef123456"];
        var exception = Record.Exception(() => ExternalMongoSafetyGuard.ValidateExistingDatabases(safeDatabases));
        Assert.Null(exception);
    }
}
