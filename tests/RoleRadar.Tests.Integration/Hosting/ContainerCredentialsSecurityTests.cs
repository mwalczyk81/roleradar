using System.Diagnostics;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Cli.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

public sealed class ContainerCredentialsSecurityTests
{
    private const string NonLoopbackCredentialedUri =
        "mongodb://sensitiveuser:TopSecretPassword123@192.0.2.1:27017/rrtest?connectTimeoutMS=1000";

    [Fact]
    public async Task Cli_RedactsPassword_WhenTargetHostIsUnreachable()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        var env = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = NonLoopbackCredentialedUri,
            ["BACKUP_PW"] = "ArchivePassword123!"
        };

        // Attempt a backup pointing to unreachable host with credentials in URI
        var result = await ContainerTestHarness.RunCliAsync(["backup", "/tmp/backup.rrbak", "--passphrase-env", "BACKUP_PW"], environment: env);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("TopSecretPassword123", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitiveuser", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TopSecretPassword123", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitiveuser", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }
}
