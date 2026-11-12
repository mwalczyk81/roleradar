#pragma warning disable CA1848, CA1873

using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>
/// Feature 015 Gate 2 (RR-72, SC-002, ADR 0050): Telemetry Privacy Gate unit tests for .NET spans and resource attributes.
/// Verifies that <see cref="RoleRadarSpanAttributeProcessor"/> (with zero dependency on <see cref="LogScrubber"/>) enforces
/// the closed attribute allowlist, explicit drop-list, URL query/fragment stripping, and span exception-event sanitization.
/// </summary>
public sealed class TelemetryPrivacyGateTests
{
    private static readonly string[] SentinelSecrets =
    [
        "SENTINEL-BEARER-TOKEN-991",
        "SENTINEL-BASIC-CRED-992",
        "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJTRU5USU5FTC1KV1QtOTkzIn0.sig",
        "SENTINEL-MONGO-PW-994",
        "SENTINEL-REFRESH-995",
        "SENTINEL-PW-996",
        "SENTINEL-JSON-SECRET-997",
        "SENTINEL-AUTH-CODE-998",
        "SENTINEL-QUERY-CODE-999",
        "SENTINEL-STATE-999",
        "SENTINEL-EX-PW-1000",
        "SENTINEL-EX-BEARER-1001",
        "SENTINEL-NOTE-BODY-1002",
        "SENTINEL-RULE-TEXT-1003",
        "SENTINEL-POSTING-DESC-1004",
        "SENTINEL-FILTER-LITERAL-1005"
    ];

    private static (List<Activity> Exported, ActivitySource Source, TracerProvider Provider) CreatePipeline(string sourceName = "RoleRadar.Test")
    {
        var exported = new List<Activity>();
        var source = new ActivitySource(sourceName);
        var provider = Sdk.CreateTracerProviderBuilder()
            .SetSampler(new AlwaysOnSampler())
            .AddSource(sourceName)
            .AddProcessor(new RoleRadarSpanAttributeProcessor())
            .AddInMemoryExporter(exported)
            .Build()!;

        return (exported, source, provider);
    }

    [Fact]
    public void HttpAttributes_AllowlistedTagsStay_QueryFragmentHeadersAndUnlistedTagsAreRemoved()
    {
        var (exported, source, provider) = CreatePipeline();
        using var _ = provider;
        using var __ = source;

        using (var activity = source.StartActivity("GET /auth/callback", ActivityKind.Server))
        {
            Assert.NotNull(activity);
            // Allowlisted HTTP attributes
            activity.SetTag("http.request.method", "GET");
            activity.SetTag("http.request.method_original", "GET");
            activity.SetTag("url.scheme", "https");
            activity.SetTag("url.path", "/auth/callback?code=SENTINEL-QUERY-CODE-999#frag");
            activity.SetTag("url.full", "https://admin:SENTINEL-MONGO-PW-994@localhost:5003/auth/callback?code=SENTINEL-QUERY-CODE-999&state=SENTINEL-STATE-999#frag");
            activity.SetTag("http.route", "/auth/callback");
            activity.SetTag("http.response.status_code", 302);
            activity.SetTag("server.address", "localhost");
            activity.SetTag("server.port", 5003);
            activity.SetTag("network.protocol.version", "1.1");
            activity.SetTag("network.peer.address", "127.0.0.1");
            activity.SetTag("error.type", "none");

            // Drop-list & forbidden attributes carrying sentinels (Rules 1-7, 9)
            activity.SetTag("url.query", "?code=SENTINEL-QUERY-CODE-999&state=SENTINEL-STATE-999");
            activity.SetTag("http.request.header.authorization", "Bearer SENTINEL-BEARER-TOKEN-991");
            activity.SetTag("http.request.header.cookie", "session=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJTRU5USU5FTC1KV1QtOTkzIn0.sig");
            activity.SetTag("http.response.header.set_cookie", "refresh_token=SENTINEL-REFRESH-995");
            activity.SetTag("password", "SENTINEL-PW-996");
            activity.SetTag("secret", "{\"client_secret\":\"SENTINEL-JSON-SECRET-997\",\"code\":\"SENTINEL-AUTH-CODE-998\"}");
            activity.SetTag("content", "SENTINEL-NOTE-BODY-1002");
            activity.SetTag("ruleText", "SENTINEL-RULE-TEXT-1003");
            activity.SetTag("description", "SENTINEL-POSTING-DESC-1004");
            activity.SetTag("odata.filter", "SENTINEL-FILTER-LITERAL-1005");
            activity.SetTag("custom.unlisted.tag", "Basic SENTINEL-BASIC-CRED-992");
        }

        provider.ForceFlush();
        var span = Assert.Single(exported);
        var tags = span.TagObjects.ToDictionary(t => t.Key, t => t.Value);

        Assert.Equal("GET", tags["http.request.method"]);
        Assert.Equal("GET", tags["http.request.method_original"]);
        Assert.Equal("https", tags["url.scheme"]);
        Assert.Equal("/auth/callback", tags["url.path"]);
        Assert.Equal("https://localhost:5003/auth/callback", tags["url.full"]);
        Assert.Equal("/auth/callback", tags["http.route"]);
        Assert.Equal(302, tags["http.response.status_code"]);
        Assert.Equal("localhost", tags["server.address"]);
        Assert.Equal(5003, tags["server.port"]);
        Assert.Equal("1.1", tags["network.protocol.version"]);
        Assert.Equal("127.0.0.1", tags["network.peer.address"]);

        Assert.False(tags.ContainsKey("url.query"));
        Assert.DoesNotContain(tags.Keys, k => k.StartsWith("http.request.header", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(tags.Keys, k => k.StartsWith("http.response.header", StringComparison.OrdinalIgnoreCase));
        Assert.False(tags.ContainsKey("password"));
        Assert.False(tags.ContainsKey("secret"));
        Assert.False(tags.ContainsKey("content"));
        Assert.False(tags.ContainsKey("ruleText"));
        Assert.False(tags.ContainsKey("description"));
        Assert.False(tags.ContainsKey("odata.filter"));
        Assert.False(tags.ContainsKey("custom.unlisted.tag"));

        var serializedTags = string.Join(" | ", tags.Select(kv => $"{kv.Key}={kv.Value}"));
        foreach (var sentinel in SentinelSecrets)
        {
            Assert.DoesNotContain(sentinel, serializedTags, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DatabaseAttributes_AllowlistedMongoTagsStay_DbQueryTextAndStatementAreRemoved()
    {
        var (exported, source, provider) = CreatePipeline("MongoDB.Driver");
        using var _ = provider;
        using var __ = source;

        using (var activity = source.StartActivity("find", ActivityKind.Client))
        {
            Assert.NotNull(activity);
            activity.SetTag("db.system.name", "mongodb");
            activity.SetTag("db.operation.name", "find");
            activity.SetTag("db.operation.summary", "find roleradar.postings");
            activity.SetTag("db.command.name", "find");
            activity.SetTag("db.namespace", "roleradar");
            activity.SetTag("db.collection.name", "postings");
            activity.SetTag("db.query.summary", "find roleradar.postings");
            activity.SetTag("server.address", "127.0.0.1");
            activity.SetTag("server.port", 27017);
            activity.SetTag("network.transport", "tcp");
            activity.SetTag("db.response.status_code", "0");
            activity.SetTag("db.mongodb.lsid", "session-1");
            activity.SetTag("db.mongodb.txn_number", 1L);
            activity.SetTag("db.mongodb.server_connection_id", 42L);
            activity.SetTag("db.mongodb.driver_connection_id", 7);
            activity.SetTag("db.mongodb.cursor_id", 0L);

            // Forbidden query text attributes
            activity.SetTag("db.query.text", "{ \"find\": \"postings\", \"filter\": { \"content\": \"SENTINEL-NOTE-BODY-1002\", \"ruleText\": \"SENTINEL-RULE-TEXT-1003\" } }");
            activity.SetTag("db.statement", "{ \"$filter\": \"SENTINEL-FILTER-LITERAL-1005\" }");
            activity.SetTag("db.connection_string", "mongodb://admin:SENTINEL-MONGO-PW-994@localhost:27017");
        }

        provider.ForceFlush();
        var span = Assert.Single(exported);
        var tags = span.TagObjects.ToDictionary(t => t.Key, t => t.Value);

        Assert.Equal("mongodb", tags["db.system.name"]);
        Assert.Equal("find", tags["db.operation.name"]);
        Assert.Equal("find", tags["db.command.name"]);
        Assert.Equal("roleradar", tags["db.namespace"]);
        Assert.Equal("postings", tags["db.collection.name"]);
        Assert.Equal("127.0.0.1", tags["server.address"]);
        Assert.Equal(27017, tags["server.port"]);
        Assert.Equal("tcp", tags["network.transport"]);
        Assert.Equal("0", tags["db.response.status_code"]);

        Assert.False(tags.ContainsKey("db.query.text"));
        Assert.False(tags.ContainsKey("db.statement"));
        Assert.False(tags.ContainsKey("db.connection_string"));

        var serializedTags = string.Join(" | ", tags.Select(kv => $"{kv.Key}={kv.Value}"));
        foreach (var sentinel in SentinelSecrets)
        {
            Assert.DoesNotContain(sentinel, serializedTags, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ExceptionEventsAndStatusDescription_RetainOnlyExceptionTypeAndErrorType_DropMessageAndStacktrace()
    {
        var (exported, source, provider) = CreatePipeline();
        using var _ = provider;
        using var __ = source;

        using (var activity = source.StartActivity("POST /connect/token", ActivityKind.Server))
        {
            Assert.NotNull(activity);
            var ex = new InvalidOperationException("Auth failed for password=SENTINEL-EX-PW-1000 Bearer SENTINEL-EX-BEARER-1001");
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddEvent(new ActivityEvent(
                "exception",
                tags: new ActivityTagsCollection
                {
                    ["exception.type"] = ex.GetType().FullName!,
                    ["exception.message"] = ex.Message,
                    ["exception.stacktrace"] = "at RoleRadar.SecretMethod(password=SENTINEL-EX-PW-1000)"
                }));
        }

        provider.ForceFlush();
        var span = Assert.Single(exported);

        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Null(span.StatusDescription);

        var tags = span.TagObjects.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(typeof(InvalidOperationException).FullName, tags["error.type"]);

        var exEvent = Assert.Single(span.Events, e => e.Name == "exception");
        var eventTags = exEvent.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal(typeof(InvalidOperationException).FullName, eventTags["exception.type"]);
        Assert.False(eventTags.ContainsKey("exception.message"));
        Assert.False(eventTags.ContainsKey("exception.stacktrace"));

        var allEventValues = string.Join(" | ", eventTags.Select(kv => $"{kv.Key}={kv.Value}"));
        Assert.DoesNotContain("SENTINEL-EX-PW-1000", allEventValues, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL-EX-BEARER-1001", allEventValues, StringComparison.Ordinal);
    }

    [Fact]
    public void TelemetryResource_ContainsOnlyServiceNameNamespaceAndVersion_NeverUsernamesEnvOrOwnerId()
    {
        var exported = new List<Activity>();
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddRoleRadarTelemetry(
            "roleradar-api",
            config,
            tracing => tracing.AddInMemoryExporter(exported));

        using var sp = services.BuildServiceProvider();
        var tracerProvider = sp.GetRequiredService<TracerProvider>();

        using (var activity = TraceIds.StartActivity("roleradar.test.resource"))
        {
            activity.SetTag("http.request.method", "GET");
        }

        tracerProvider.ForceFlush();
        Assert.NotEmpty(exported);

        // Inspect resource attributes via TracerProvider.GetResource()
        var resource = tracerProvider.GetResource();
        var resourceAttrs = resource.Attributes.ToDictionary(a => a.Key, a => a.Value);

        Assert.Equal("roleradar-api", resourceAttrs["service.name"]);
        Assert.Equal("roleradar", resourceAttrs["service.namespace"]);
        Assert.True(resourceAttrs.ContainsKey("service.version"));
        Assert.False(string.IsNullOrWhiteSpace(resourceAttrs["service.version"]?.ToString()));

        Assert.DoesNotContain(resourceAttrs.Keys, k => k.Contains("owner", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resourceAttrs.Keys, k => k.StartsWith("process.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resourceAttrs.Keys, k => k.StartsWith("host.", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(resourceAttrs.Keys, k => k.StartsWith("os.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RoleRadarSpanAttributeProcessor_DoesNotReferenceLogScrubber()
    {
        // ADR 0050: Gate 2 span protection is structural and completely independent of LogScrubber (RR-86 in Gate 3).
        var sourcePath = Path.Combine(
            Path.GetDirectoryName(typeof(TelemetryPrivacyGateTests).Assembly.Location)!,
            "..", "..", "..", "..", "..", "src", "RoleRadar.Observability", "RoleRadarSpanAttributeProcessor.cs");
        var normalized = Path.GetFullPath(sourcePath);
        var code = File.ReadAllText(normalized);
        var withoutComments = string.Join(
            "\n",
            code.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        Assert.DoesNotContain("LogScrubber", withoutComments, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivityEventsField_MustExist_AndAddRoleRadarTelemetry_FailsClosedIfNull()
    {
        Assert.NotNull(RoleRadarSpanAttributeProcessor.ActivityEventsField);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TelemetrySetup.ValidateActivityEventsField(null));

        Assert.Contains("System.Diagnostics.Activity._events", ex.Message, StringComparison.Ordinal);
        Assert.Contains("RoleRadar telemetry", ex.Message, StringComparison.Ordinal);
    }

    private static (List<LogRecord> Exported, ILoggerFactory Factory) CreateLoggingPipeline()
    {
        var exported = new List<LogRecord>();
        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = false;
                logging.AddProcessor(new RoleRadarLogScrubbingProcessor());
                logging.AddInMemoryExporter(exported);
            });
        });

        return (exported, factory);
    }

    [Fact]
    public void LogScrubbingProcessor_ScrubsBodyAndFormattedMessage_WhenSentinelsArePresent()
    {
        var (exported, factory) = CreateLoggingPipeline();
        using var _ = factory;
        var logger = factory.CreateLogger("PrivacyGate.Tests");

        var sensitiveBody = "User authenticated with password=SENTINEL-EX-PW-1000 and token Bearer SENTINEL-EX-BEARER-1001"; // gitleaks:allow (fictional test vector, not a credential)
        logger.LogInformation("{Message}", sensitiveBody);

        var record = Assert.Single(exported);
        Assert.DoesNotContain("SENTINEL-EX-PW-1000", record.Body ?? string.Empty, StringComparison.Ordinal); // gitleaks:allow (fictional test vector, not a credential)
        Assert.DoesNotContain("SENTINEL-EX-BEARER-1001", record.Body ?? string.Empty, StringComparison.Ordinal); // gitleaks:allow (fictional test vector, not a credential)
        Assert.DoesNotContain("SENTINEL-EX-PW-1000", record.FormattedMessage ?? string.Empty, StringComparison.Ordinal); // gitleaks:allow (fictional test vector, not a credential)
        Assert.DoesNotContain("SENTINEL-EX-BEARER-1001", record.FormattedMessage ?? string.Empty, StringComparison.Ordinal); // gitleaks:allow (fictional test vector, not a credential)
    }

    [Fact]
    public void LogScrubbingProcessor_ScrubsAttributes_RedactingSensitiveKeys_AndSanitizingStringValues()
    {
        var (exported, factory) = CreateLoggingPipeline();
        using var _ = factory;
        var logger = factory.CreateLogger("PrivacyGate.Tests");

        logger.LogInformation(
            "Operation started with {Password} and {Authorization} and {CustomUnlisted}",
            "SENTINEL-PW-996", // gitleaks:allow (fictional test vector, not a credential)
            "Bearer SENTINEL-BEARER-TOKEN-991", // gitleaks:allow (fictional test vector, not a credential)
            "Embedded password=SENTINEL-EX-PW-1000 value"); // gitleaks:allow (fictional test vector, not a credential)

        var record = Assert.Single(exported);
        var attrs = record.Attributes?.ToDictionary(k => k.Key, k => k.Value?.ToString() ?? string.Empty)
            ?? new Dictionary<string, string>();

        foreach (var sentinel in SentinelSecrets)
        {
            foreach (var kv in attrs)
            {
                Assert.DoesNotContain(sentinel, kv.Value, StringComparison.Ordinal);
            }
        }

        if (attrs.TryGetValue("Password", out var pwValue))
        {
            Assert.Equal(LogScrubber.Redacted, pwValue);
        }
        if (attrs.TryGetValue("Authorization", out var authValue))
        {
            Assert.Equal(LogScrubber.Redacted, authValue);
        }
    }

    private sealed class DistinctiveSecretLeakException(string message) : Exception(message);

    [Fact]
    public void LogScrubbingProcessor_ScrubsException_NullsException_AndAddsScrubbedTypeAndMessageAttributesWithoutStacktrace()
    {
        var (exported, factory) = CreateLoggingPipeline();
        using var _ = factory;
        var logger = factory.CreateLogger("PrivacyGate.Tests");

        var ex = new DistinctiveSecretLeakException("Auth failed: password=SENTINEL-EX-PW-1000 Bearer SENTINEL-EX-BEARER-1001"); // gitleaks:allow (fictional test vector, not a credential)
        logger.LogError(ex, "An unhandled exception occurred");

        var record = Assert.Single(exported);
        Assert.Null(record.Exception);

        var attrs = record.Attributes?.ToDictionary(k => k.Key, k => k.Value?.ToString() ?? string.Empty)
            ?? new Dictionary<string, string>();

        Assert.True(attrs.TryGetValue("exception.type", out var exType));
        Assert.Equal(typeof(DistinctiveSecretLeakException).FullName, exType);

        Assert.True(attrs.TryGetValue("exception.message", out var exMsg));
        Assert.DoesNotContain("SENTINEL-EX-PW-1000", exMsg, StringComparison.Ordinal); // gitleaks:allow (fictional test vector, not a credential)
        Assert.DoesNotContain("SENTINEL-EX-BEARER-1001", exMsg, StringComparison.Ordinal); // gitleaks:allow (fictional test vector, not a credential)
        Assert.Contains(LogScrubber.Redacted, exMsg, StringComparison.Ordinal);

        Assert.False(attrs.ContainsKey("exception.stacktrace"));
    }

    [Fact]
    public void LogPipeline_WhenScopesUsed_ScopesAreNotExported_PreventingSentinelLeak()
    {
        var (exported, factory) = CreateLoggingPipeline();
        using var _ = factory;
        var logger = factory.CreateLogger("PrivacyGate.Tests");

        using (logger.BeginScope("Scope with password={Password} and token Bearer {Token}", "SENTINEL-PW-996", "SENTINEL-BEARER-TOKEN-991")) // gitleaks:allow
        {
            logger.LogInformation("Operation inside scope");
        }

        var record = Assert.Single(exported);
        var scopeCount = 0;
        var scopeValues = new List<string>();
        record.ForEachScope((scope, _) =>
        {
            scopeCount++;
            if (scope.Scope != null)
            {
                scopeValues.Add(scope.Scope.ToString() ?? string.Empty);
            }
            foreach (KeyValuePair<string, object?> kvp in scope)
            {
                scopeValues.Add($"{kvp.Key}={kvp.Value}");
            }
        }, (object?)null);

        Assert.Equal(0, scopeCount);
        var combinedScopes = string.Join(" | ", scopeValues);
        Assert.DoesNotContain("SENTINEL-PW-996", combinedScopes, StringComparison.Ordinal); // gitleaks:allow
        Assert.DoesNotContain("SENTINEL-BEARER-TOKEN-991", combinedScopes, StringComparison.Ordinal); // gitleaks:allow
    }
}
