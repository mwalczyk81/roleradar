using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using RoleRadar.Observability;
using RoleRadar.Tests;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

/// <summary>
/// Feature 015 Gate 5 fix (RR-71, ADR 0051): the T074 dashboard check found a PKCE code_verifier in the clear in an Identity log
/// record (Duende's token request validation log). Duende masks the code and the client secret itself but not the verifier,
/// and ADR 0051 says PKCE verifiers must not reach an OTLP collector. The record is emitted when Duende logs at Debug or Trace
/// (the local AppHost runs Identity as Development), which the census only checked at default levels.
/// </summary>
public sealed class IdentityCodeVerifierLogContractTests
{
    // 43 to 128 unreserved characters, as RFC 7636 requires; the sentinel must reach neither sink.
    private const string SentinelVerifier = "SENTINEL-VERIFIER-7b3e91d04f6a2c85e1d09a3b-0123456789";

    private static async Task<(string Console, IReadOnlyList<LogRecord> Otlp, IReadOnlyList<RawLogEntry> PreScrub)> RunAsync(string duendeLevel)
    {
        var console = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("identity", LogFormat.Json));
        var preScrub = new PreScrubLogCapture();
        var exported = new List<LogRecord>();

        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Duende"] = duendeLevel,
            });
            builder.Logging.AddProvider(console);
            builder.Logging.AddProvider(preScrub);

            // The same shape as LoggingSetup's OTLP pipeline: the scrubbing processor first, then the exporter.
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = false;
                logging.AddProcessor(new RoleRadarLogScrubbingProcessor());
                logging.AddInMemoryExporter(exported);
            });
        });

        try
        {
            await IdentityContractTestServer.LoginAndGetIdTokenAsync(client, app.Services, SentinelVerifier);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }

        return (console.Text, exported.ToList(), preScrub.Entries);
    }

    /// <summary>Everything an OTLP exporter would serialise for a record: body, formatted message and every attribute value.</summary>
    private static string Serialised(LogRecord record)
    {
        var parts = new List<string> { Convert.ToString(record.Body, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, record.FormattedMessage ?? string.Empty };
        foreach (var attribute in record.Attributes ?? [])
        {
            parts.Add(attribute.Key);
            parts.Add(Convert.ToString(attribute.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return string.Join('\n', parts);
    }

    [Theory]
    [InlineData("Information")]
    [InlineData("Debug")]
    [InlineData("Trace")]
    public async Task TheCodeVerifier_ReachesNeitherTheConsoleNorTheOtlpRecords_WhenDuendeLogsVerbosely(string duendeLevel)
    {
        var (console, otlp, preScrub) = await RunAsync(duendeLevel);

        // Non-vacuous: Duende really does emit the verifier before scrubbing at this level.
        var rawRecords = preScrub.Where(e => e.Message.Contains(SentinelVerifier, StringComparison.Ordinal)
            || e.Properties.Any(p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture)?.Contains(SentinelVerifier, StringComparison.Ordinal) == true)).ToList();
        Assert.NotEmpty(rawRecords);

        Assert.DoesNotContain(SentinelVerifier, console, StringComparison.Ordinal);
        Assert.DoesNotContain(otlp, record => Serialised(record).Contains(SentinelVerifier, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheNonSensitiveRawFields_StayVisible_OnBothSinks()
    {
        var (console, otlp, _) = await RunAsync("Debug");

        Assert.Contains("authorization_code", console, StringComparison.Ordinal);
        Assert.Contains("redirect_uri", console, StringComparison.Ordinal);
        Assert.Contains(otlp, record => Serialised(record).Contains("authorization_code", StringComparison.Ordinal)
            && Serialised(record).Contains("redirect_uri", StringComparison.Ordinal));
        Assert.Contains(LogScrubber.Redacted, console, StringComparison.Ordinal);
    }
}
