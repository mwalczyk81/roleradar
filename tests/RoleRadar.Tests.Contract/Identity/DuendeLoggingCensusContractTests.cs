using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

/// <summary>
/// A log entry captured before formatters or OpenTelemetry processors run.
/// </summary>
internal sealed class RawLogEntry
{
    public required LogLevel LogLevel { get; init; }
    public required string Category { get; init; }
    public required EventId EventId { get; init; }
    public required string Message { get; init; }
    public Exception? Exception { get; init; }
    public IReadOnlyList<KeyValuePair<string, object?>> Properties { get; init; } = [];
}

/// <summary>
/// Pre-scrub log capture harness: registers as an <see cref="ILoggerProvider"/> directly on
/// <see cref="ILoggingBuilder"/> in <see cref="IdentityContractTestServer.StartHostAsync"/>.
/// Because it receives calls directly from <see cref="ILogger.Log{TState}"/>, it captures
/// the raw category, log level, event ID, structured state, and formatted message before
/// <see cref="RoleRadarConsoleFormatter"/> or <see cref="RoleRadarLogScrubbingProcessor"/>
/// runs <see cref="LogScrubber.ScrubText"/>.
/// </summary>
internal sealed class PreScrubLogCapture : ILoggerProvider
{
    private readonly List<RawLogEntry> _entries = new();
    private readonly object _gate = new();

    public IReadOnlyList<RawLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(PreScrubLogCapture capture, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            var props = new List<KeyValuePair<string, object?>>();
            if (state is IEnumerable<KeyValuePair<string, object?>> kvs)
            {
                props.AddRange(kvs);
            }

            lock (capture._gate)
            {
                capture._entries.Add(new RawLogEntry
                {
                    LogLevel = logLevel,
                    Category = category,
                    EventId = eventId,
                    Message = msg,
                    Exception = exception,
                    Properties = props
                });
            }
        }
    }
}

/// <summary>
/// Contract tests providing empirical census evidence for Duende IdentityServer logging behavior
/// under RoleRadar's default logging levels (LoggingSetup.DefaultLevels).
/// </summary>
public sealed class DuendeLoggingCensusContractTests
{
    private const string SentinelClientId = "SENTINEL-CLIENT-912a";
    private const string SentinelState = "SENTINEL-STATE-481b";
    private const string SentinelCodeChallenge = "SENTINEL-CHALLENGE-392c";
    private const string SentinelNonce = "SENTINEL-NONCE-104d";
    private const string SentinelRedirectUri = "https://localhost:5003/sentinel-callback";
    private const string SentinelRefreshToken = "SENTINEL-REFRESH-TOKEN-592e";
    private const string SentinelClientSecret = "SENTINEL-CLIENT-SECRET-831f";

    [Fact]
    public async Task AuthorizeRequest_AtDefaultLevels_EmitsNoSentinelsFromDuendeCategories_PreScrub()
    {
        var capture = new PreScrubLogCapture();
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(
            builder => builder.Logging.AddProvider(capture));

        try
        {
            // Send GET /connect/authorize with fictional sentinel values
            var url = $"/connect/authorize?client_id={SentinelClientId}" +
                      $"&response_type=code" +
                      $"&scope=openid" +
                      $"&redirect_uri={Uri.EscapeDataString(SentinelRedirectUri)}" +
                      $"&state={SentinelState}" +
                      $"&code_challenge={SentinelCodeChallenge}" +
                      $"&code_challenge_method=S256" +
                      $"&nonce={SentinelNonce}";

            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);

            // Assert that under default configuration, filtering at the source ensures NO Duende category
            // emits sensitive authorize request parameters (state, code_challenge, nonce, redirect_uri) before scrubbing.
            var sensitiveSentinels = new[] { SentinelState, SentinelCodeChallenge, SentinelNonce, SentinelRedirectUri };

            Assert.DoesNotContain(capture.Entries, e =>
                e.Category.StartsWith("Duende", StringComparison.Ordinal)
                && sensitiveSentinels.Any(s =>
                    e.Message.Contains(s, StringComparison.Ordinal)
                    || e.Properties.Any(p => p.Value?.ToString()?.Contains(s, StringComparison.Ordinal) == true)));
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }
    }

    [Fact]
    public async Task AuthorizeRequest_WhenDuendeLoggingOverriddenToInformation_EmitsRawRequestJson_AndRule5ScrubsIt()
    {
        var capture = new PreScrubLogCapture();
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(
            builder => builder.Logging.AddProvider(capture),
            extraArgs:
            [
                "--Logging:LogLevel:Duende.IdentityServer.Validation.AuthorizeRequestValidator=Information",
                "--Logging:LogLevel:Duende.IdentityServer.Endpoints.AuthorizeEndpoint=Information"
            ]);

        try
        {
            var url = $"/connect/authorize?client_id={SentinelClientId}" +
                      $"&response_type=code" +
                      $"&scope=openid" +
                      $"&redirect_uri={Uri.EscapeDataString(SentinelRedirectUri)}" +
                      $"&state={SentinelState}" +
                      $"&code_challenge={SentinelCodeChallenge}" +
                      $"&code_challenge_method=S256" +
                      $"&nonce={SentinelNonce}";

            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);

            // 1. Proves the filter (not the scrubber) was responsible for suppressing them in the default test:
            // when explicitly overridden by operator configuration, Duende emits the raw request JSON text.
            var validatorEntry = Assert.Single(capture.Entries, e =>
                e.Category == "Duende.IdentityServer.Validation.AuthorizeRequestValidator"
                && e.LogLevel == LogLevel.Information);

            Assert.Contains(SentinelClientId, validatorEntry.Message, StringComparison.Ordinal);
            Assert.Contains(SentinelState, validatorEntry.Message, StringComparison.Ordinal);
            Assert.Contains(SentinelCodeChallenge, validatorEntry.Message, StringComparison.Ordinal);
            Assert.Contains(SentinelNonce, validatorEntry.Message, StringComparison.Ordinal);
            Assert.Contains(SentinelRedirectUri, validatorEntry.Message, StringComparison.Ordinal);

            // 2. Proves that Rule 5 (JsonPair) in LogScrubber redacts these JSON fields if overridden to Information
            var scrubbedValidatorMsg = LogScrubber.ScrubText(validatorEntry.Message);
            Assert.DoesNotContain(SentinelState, scrubbedValidatorMsg, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelCodeChallenge, scrubbedValidatorMsg, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelNonce, scrubbedValidatorMsg, StringComparison.Ordinal);
            Assert.Contains("\"state\": \"[redacted]\"", scrubbedValidatorMsg, StringComparison.Ordinal);
            Assert.Contains("\"code_challenge\": \"[redacted]\"", scrubbedValidatorMsg, StringComparison.Ordinal);
            Assert.Contains("\"nonce\": \"[redacted]\"", scrubbedValidatorMsg, StringComparison.Ordinal);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }
    }

    [Fact]
    public async Task FailedTokenRequest_DoesNotEmitClientSecretOrRefreshToken_AtDefaultLevels()
    {
        var capture = new PreScrubLogCapture();
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(
            builder => builder.Logging.AddProvider(capture));

        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = "bad-auth-code-12345",
                ["client_id"] = "roleradar-web",
                ["client_secret"] = SentinelClientSecret,
                ["refresh_token"] = SentinelRefreshToken,
                ["redirect_uri"] = "https://localhost:5003/auth/callback"
            });

            var response = await client.PostAsync("/connect/token", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            // 1. Verify client failure is logged at Error without leaking client secret or refresh token values
            var secretFailEntry = Assert.Single(capture.Entries, e =>
                e.Category == "Duende.IdentityServer.Validation.ClientSecretValidator"
                && e.LogLevel == LogLevel.Error);

            Assert.Equal("Client secret validation failed for client: roleradar-web.", secretFailEntry.Message);

            // 2. Neither the client secret nor refresh token is emitted in ANY captured pre-scrub log entry
            Assert.DoesNotContain(capture.Entries, e =>
                e.Message.Contains(SentinelClientSecret, StringComparison.Ordinal)
                || e.Properties.Any(p => p.Value?.ToString()?.Contains(SentinelClientSecret, StringComparison.Ordinal) == true));

            Assert.DoesNotContain(capture.Entries, e =>
                e.Message.Contains(SentinelRefreshToken, StringComparison.Ordinal)
                || e.Properties.Any(p => p.Value?.ToString()?.Contains(SentinelRefreshToken, StringComparison.Ordinal) == true));
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }
    }
}
