#pragma warning disable CA1848, CA1873, CA1727

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>
/// Feature 015 Gate 5 fix (RR-71, ADR 0051): a structured log attribute that is an object (Duende passes its token request
/// validation log as <c>{@details}</c>) used to bypass the OTLP scrubbing processor, which only scrubbed string values, so the
/// exporter serialised the object with its own ToString() and a PKCE code_verifier left in the clear. These tests run the real
/// <c>AddRoleRadarLogging</c> pipeline (scrubbing processor, then an in-memory exporter) with the exact payload shape.
/// </summary>
public sealed class LogScrubbingObjectAttributeTests
{
    private static readonly int[] Ids = [1, 2, 3];

    private const string SentinelVerifier = "SENTINEL-VERIFIER-7b3e91d04f6a2c85e1d09a3b-0123456789";

    /// <summary>Duende's TokenRequestValidationLog: it masks the code and the client secret itself, not the code_verifier.</summary>
    private sealed class DuendeLikeValidationLog
    {
        public override string ToString() => $$"""
            {
              "ClientId": "roleradar-web",
              "ClientName": "RoleRadar Web Application",
              "GrantType": "authorization_code",
              "AuthorizationCode": "****44-1",
              "RefreshToken": "********",
              "Raw": {
                "client_id": "roleradar-web",
                "client_secret": "***REDACTED***",
                "grant_type": "authorization_code",
                "code": "***REDACTED***",
                "redirect_uri": "https://localhost:5003/auth/callback",
                "code_verifier": "{{SentinelVerifier}}"
              }
            }
            """;
    }

    private static (ILogger Logger, List<LogRecord> Exported, ServiceProvider Provider) CreatePipeline()
    {
        var exported = new List<LogRecord>();
        var provider = new ServiceCollection()
            .AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddRoleRadarLogging("roleradar-test", LogStream.StdErr, configureLogging: options => options.AddInMemoryExporter(exported));
            })
            .BuildServiceProvider();
        return (provider.GetRequiredService<ILoggerFactory>().CreateLogger("Duende.IdentityServer.Validation.TokenRequestValidator"), exported, provider);
    }

    /// <summary>Everything an exporter would serialise: body, formatted message, and every attribute key and value.</summary>
    private static string Serialised(LogRecord record) =>
        string.Join('\n', new[] { $"{record.Body}", record.FormattedMessage ?? string.Empty }
            .Concat((record.Attributes ?? []).SelectMany(a => new[] { a.Key, $"{a.Value}" })));

    [Fact]
    public void AnObjectAttribute_WithTheCodeVerifier_IsScrubbedBeforeExport_AndTheOtherRawFieldsStayVisible()
    {
        var (logger, exported, provider) = CreatePipeline();
        using var _ = provider;

        logger.LogInformation("{Message}: {@details}", "Token request validation success", new DuendeLikeValidationLog());
        provider.GetRequiredService<ILoggerFactory>().Dispose();

        var record = Assert.Single(exported);
        var text = Serialised(record);
        Assert.DoesNotContain(SentinelVerifier, text, StringComparison.Ordinal);
        Assert.Contains(LogScrubber.Redacted, text, StringComparison.Ordinal);
        Assert.Contains("authorization_code", text, StringComparison.Ordinal);
        Assert.Contains("https://localhost:5003/auth/callback", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectAttribute_IsExportedAsScrubbedText_NotAsTheObject()
    {
        var (logger, exported, provider) = CreatePipeline();
        using var _ = provider;

        logger.LogInformation("{Message}: {@details}", "Token request validation success", new DuendeLikeValidationLog());
        provider.GetRequiredService<ILoggerFactory>().Dispose();

        var details = Assert.Single(Assert.Single(exported).Attributes!, a => a.Key == "@details");
        Assert.IsType<string>(details.Value);
    }

    [Fact]
    public void ACollectionOfObjects_IsScrubbedElementByElement()
    {
        var (logger, exported, provider) = CreatePipeline();
        using var _ = provider;

        object items = new object[] { new DuendeLikeValidationLog(), "plain text" };
        object lines = new[] { new DuendeLikeValidationLog().ToString() };
        logger.LogInformation("{Items}", items);
        logger.LogInformation("{Lines}", lines);
        provider.GetRequiredService<ILoggerFactory>().Dispose();

        Assert.Equal(2, exported.Count);
        Assert.All(exported, record => Assert.DoesNotContain(SentinelVerifier, Serialised(record), StringComparison.Ordinal));
    }

    [Fact]
    public void ASensitiveKey_WithAnObjectValue_IsRedactedWholesale()
    {
        var (logger, exported, provider) = CreatePipeline();
        using var _ = provider;

        logger.LogInformation("{code_verifier}", new DuendeLikeValidationLog());
        provider.GetRequiredService<ILoggerFactory>().Dispose();

        var attribute = Assert.Single(Assert.Single(exported).Attributes!, a => a.Key == "code_verifier");
        Assert.Equal(LogScrubber.Redacted, attribute.Value);
    }

    [Fact]
    public void Numbers_And_Booleans_KeepTheirType()
    {
        var (logger, exported, provider) = CreatePipeline();
        using var _ = provider;

        logger.LogInformation("{Count} {Flag} {Elapsed} {Ids}", 7, true, 1.5, Ids);
        provider.GetRequiredService<ILoggerFactory>().Dispose();

        var attributes = Assert.Single(exported).Attributes!.ToDictionary(a => a.Key, a => a.Value);
        Assert.Equal(7, attributes["Count"]);
        Assert.Equal(true, attributes["Flag"]);
        Assert.Equal(1.5, attributes["Elapsed"]);
        Assert.Equal(Ids, Assert.IsType<int[]>(attributes["Ids"]));
    }
}
