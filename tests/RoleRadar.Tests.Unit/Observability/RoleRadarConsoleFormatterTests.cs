// Tests log fixed templates to exercise the formatter; LoggerMessage delegates would add nothing here.
#pragma warning disable CA1848, CA1873

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>Record shape from specs/013-operability/contracts/log-records.md.</summary>
public sealed partial class RoleRadarConsoleFormatterTests
{
    private const string Jwt = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTkyIn0.c2lnbmF0dXJlLWZpY3Rpb25hbA"; // gitleaks:allow (fictional redaction test vector, not a credential)

    private static (FormatterLogCapture Capture, ILogger Logger) Create(LogFormat format, string service = "api", string category = "RoleRadar.Test")
    {
        var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create(service, format));
        return (capture, capture.CreateLogger(category));
    }

    private static JsonElement SingleJson(FormatterLogCapture capture)
    {
        var line = Assert.Single(capture.Lines);
        return JsonDocument.Parse(line).RootElement;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$")]
    private static partial Regex UtcMillis();

    [Fact]
    public void Json_WritesOneObjectWithTheContractFields()
    {
        var (capture, logger) = Create(LogFormat.Json);

        logger.LogInformation(new EventId(42, "ThingHappened"), "Posting {PostingId} moved to {Disposition}", "p-1", "screened_out");

        var json = SingleJson(capture);
        Assert.Matches(UtcMillis(), json.GetProperty("ts").GetString());
        Assert.Equal("info", json.GetProperty("level").GetString());
        Assert.Equal("api", json.GetProperty("service").GetString());
        Assert.Equal("RoleRadar.Test", json.GetProperty("category").GetString());
        Assert.Equal("Posting p-1 moved to screened_out", json.GetProperty("msg").GetString());
        Assert.Equal("ThingHappened", json.GetProperty("event").GetString());
        Assert.Equal(42, json.GetProperty("eventId").GetInt32());
        Assert.Equal("p-1", json.GetProperty("props").GetProperty("PostingId").GetString());
        Assert.False(json.GetProperty("props").TryGetProperty("{OriginalFormat}", out _));
        Assert.False(json.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public void Json_TakesCorrelationIdFromScope()
    {
        var (capture, logger) = Create(LogFormat.Json);

        using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = "test-corr-0001" }))
        {
            logger.LogInformation("inside");
        }

        Assert.Equal("test-corr-0001", SingleJson(capture).GetProperty("correlationId").GetString());
    }

    [Fact]
    public void Json_DropsAspNetHostingScopeNoise()
    {
        var (capture, logger) = Create(LogFormat.Json);

        using (logger.BeginScope(new Dictionary<string, object?> { ["RequestId"] = "0HN1", ["RequestPath"] = "/x", ["SpanId"] = "s", ["TraceId"] = "t", ["ParentId"] = "p", ["ConnectionId"] = "c" }))
        {
            logger.LogInformation("inside");
        }

        Assert.False(SingleJson(capture).TryGetProperty("props", out _));
    }

    [Fact]
    public void Json_WritesTheErrorObject()
    {
        var (capture, logger) = Create(LogFormat.Json);

        logger.LogError(new InvalidOperationException("boom"), "failed");

        var error = SingleJson(capture).GetProperty("error");
        Assert.Equal("System.InvalidOperationException", error.GetProperty("type").GetString());
        Assert.Equal("boom", error.GetProperty("message").GetString());
        Assert.Contains("boom", error.GetProperty("stack").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "trace")]
    [InlineData(LogLevel.Debug, "debug")]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Error, "error")]
    [InlineData(LogLevel.Critical, "fatal")]
    public void Json_MapsLevels(LogLevel level, string expected)
    {
        var (capture, logger) = Create(LogFormat.Json);

        logger.Log(level, "x");

        Assert.Equal(expected, SingleJson(capture).GetProperty("level").GetString());
    }

    [Fact]
    public void Text_WritesOneReadableLine()
    {
        var (capture, logger) = Create(LogFormat.Text, service: "identity");

        using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = "test-corr-0001" }))
        {
            logger.LogWarning("Posting {PostingId} moved", "p-1");
        }

        var line = Assert.Single(capture.Lines);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z warn identity \[test-corr-0001\] Posting p-1 moved PostingId=p-1$", line);
    }

    [Fact]
    public void Text_WithoutCorrelationId_UsesADash()
    {
        var (capture, logger) = Create(LogFormat.Text, service: "cli");

        logger.LogInformation("started");

        Assert.Matches(@"^\S+ info cli \[-\] started$", Assert.Single(capture.Lines));
    }

    [Theory]
    [InlineData(LogFormat.Json)]
    [InlineData(LogFormat.Text)]
    public void EmbeddedNewlines_StillProduceOneLine(LogFormat format)
    {
        var (capture, logger) = Create(format);

        logger.LogError(new InvalidOperationException("line one\nline two"), "first\r\nsecond {Value}", "a\nb");

        Assert.Single(capture.Lines);
    }

    [Theory]
    [InlineData(LogFormat.Json)]
    [InlineData(LogFormat.Text)]
    public void EveryField_IsScrubbed(LogFormat format)
    {
        var (capture, logger) = Create(format);

        using (logger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = "test-corr-0001", ["Upstream"] = $"Bearer {Jwt}" }))
        {
            logger.LogError(
                new InvalidOperationException($"connect failed mongodb://{SecretVectors.User}:{SecretVectors.Password}@mongo.test:27017"),
                "token {Token} and header Bearer SENTINEL-TOKEN-7f3a for {Path}",
                "SENTINEL-TOKEN-PROP",
                "/auth/callback?code=SENTINEL-CODE&state=SENTINEL-STATE");
        }

        var text = capture.Text;
        Assert.Single(capture.Lines);
        foreach (var secret in new[] { Jwt, SecretVectors.Password, SecretVectors.User, "SENTINEL-TOKEN-7f3a", "SENTINEL-TOKEN-PROP", "SENTINEL-CODE", "SENTINEL-STATE" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(null, LogFormat.Text)]
    [InlineData("", LogFormat.Text)]
    [InlineData("text", LogFormat.Text)]
    [InlineData("json", LogFormat.Json)]
    [InlineData("JSON", LogFormat.Json)]
    [InlineData(" json ", LogFormat.Json)]
    [InlineData("Development", LogFormat.Text)]
    [InlineData("bogus", LogFormat.Text)]
    public void ResolveFormat_DependsOnlyOnTheRoleRadarVariableValue(string? value, LogFormat expected)
    {
        // The format is a pure function of ROLERADAR_LOG_FORMAT; no environment name (IsDevelopment) is consulted,
        // which keeps DemoEnvironmentParityTests green.
        Assert.Equal(expected, LoggingSetup.ResolveFormat(value));
    }

    [Theory]
    [InlineData(LogStream.Split, LogLevel.Warning)]
    [InlineData(LogStream.StdErr, LogLevel.Trace)]
    public void Setup_ConfiguresTheConsoleStreamAndFormatter(LogStream stream, LogLevel expectedStdErrThreshold)
    {
        // StdErr (CLI, MCP) sends every level to stderr, so stdout stays reserved (Principle VI).
        // Split (API, Identity) sends warn and above to stderr, info and below to stdout.
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddRoleRadarLogging("mcp", stream));
        using var provider = services.BuildServiceProvider();

        var console = provider.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value;
        Assert.Equal(expectedStdErrThreshold, console.LogToStandardErrorThreshold);
        Assert.Equal(RoleRadarConsoleFormatter.FormatterName, console.FormatterName);

        var formatterOptions = provider.GetRequiredService<IOptionsMonitor<RoleRadarFormatterOptions>>().CurrentValue;
        Assert.Equal("mcp", formatterOptions.Service);
    }
}
