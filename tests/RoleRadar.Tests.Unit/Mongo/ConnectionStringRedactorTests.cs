using FsCheck;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Mongo;

public sealed class ConnectionStringRedactorTests
{
    [Theory]
    [InlineData("mongodb://myuser:mypassword@localhost:27017/?replicaSet=rs0&authSource=admin", "localhost:27017 (rs0)")]
    [InlineData("mongodb://app_user:s3cr%40t_p%40ss@host1:27017,host2:27018/?replicaSet=myCluster&ssl=true&tlsCertificateKeyFile=/secret/cert.pem", "host1:27017,host2:27018 (myCluster)")]
    [InlineData("mongodb+srv://admin:SuperSecretKey!@cluster0.abcde.mongodb.net/?retryWrites=true&w=majority", "cluster0.abcde.mongodb.net")]
    [InlineData("mongodb://user:topsecret@[::1]:27017/?replicaSet=rs_ipv6", "[::1]:27017 (rs_ipv6)")]
    [InlineData("mongodb://[2001:db8::1]:27017,[2001:db8::2]:27018/?replicaSet=rs_v6multi", "[2001:db8::1]:27017,[2001:db8::2]:27018 (rs_v6multi)")]
    [InlineData("mongodb://localhost:27017", "localhost:27017")]
    [InlineData("mongodb://localhost:27017/?replicaSet=rs0", "localhost:27017 (rs0)")]
    [InlineData("mongodb://user:p%40ssword@localhost:27017", "localhost:27017")]
    public void Redact_StripsCredentialsAndQueryParameters_RetainsHostPortAndReplicaSet(string input, string expected)
    {
        var redacted = ConnectionStringRedactor.Redact(input);
        Assert.Equal(expected, redacted);
    }

    [Theory]
    [InlineData("not a valid uri ::: random junk")]
    [InlineData("http://invalid-scheme")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Redact_InvalidOrEmptyUri_ReturnsRedactedToken(string? input)
    {
        var redacted = ConnectionStringRedactor.Redact(input);
        Assert.Equal("<redacted>", redacted);
    }

    [Theory]
    [InlineData("mongodb://user:super_secret_password@db.example.com:27017/?replicaSet=rs0&authMechanism=SCRAM-SHA-256", "super_secret_password")]
    [InlineData("mongodb://user:P%40ssw0rd!%23@db.example.com:27017/?replicaSet=rs0", "P%40ssw0rd!%23")]
    [InlineData("mongodb+srv://user:MySecretToken99@cluster.example.com/?tlsCertificateKeyFile=/secrets/key.pem", "MySecretToken99")]
    [InlineData("mongodb+srv://user:MySecretToken99@cluster.example.com/?tlsCertificateKeyFile=/secrets/key.pem", "tlsCertificateKeyFile")]
    [InlineData("mongodb+srv://user:MySecretToken99@cluster.example.com/?tlsCertificateKeyFile=/secrets/key.pem", "secrets/key.pem")]
    public void Redact_SensitiveMaterial_NeverPresentInOutput(string connectionString, string secretSubstring)
    {
        var redacted = ConnectionStringRedactor.Redact(connectionString);
        Assert.DoesNotContain(secretSubstring, redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PropertyTest_ConnectionStringRedactor_GeneratedPasswordNeverAppearsInRedactedOutput()
    {
        var property = FsCheck.Fluent.Prop.ForAll<string>(password =>
        {
            if (string.IsNullOrEmpty(password) || password.Length < 4 || password.Any(char.IsControl))
            {
                return true;
            }

            var escapedPass = Uri.EscapeDataString(password);
            var connStr = $"mongodb://appuser:{escapedPass}@db-node-1.internal:27017,db-node-2.internal:27018/?replicaSet=rs_prod&authSource=admin&tlsCertificateKeyFile=/vault/{escapedPass}.pem";

            var redacted = ConnectionStringRedactor.Redact(connStr);

            // Neither the raw password nor the escaped password nor auth parameters should appear
            var rawFound = redacted.Contains(password, StringComparison.OrdinalIgnoreCase);
            var escapedFound = redacted.Contains(escapedPass, StringComparison.OrdinalIgnoreCase);
            var authSourceFound = redacted.Contains("authSource", StringComparison.OrdinalIgnoreCase);
            var tlsFound = redacted.Contains("tlsCertificateKeyFile", StringComparison.OrdinalIgnoreCase);

            return !rawFound && !escapedFound && !authSourceFound && !tlsFound;
        });

        property.QuickCheckThrowOnFailure();
    }
}
