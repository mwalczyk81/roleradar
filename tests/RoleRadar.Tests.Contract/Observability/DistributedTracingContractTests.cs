using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Trace;
using RoleRadar.Observability;
using RoleRadar.Tests.Contract.Identity;
using Xunit;

namespace RoleRadar.Tests.Contract.Observability;

/// <summary>
/// Feature 015 US1 &amp; US2 (FR-001, FR-002, FR-012, FR-014, SC-001, SC-002, ADR 0050):
/// In-process contract tests (no Docker / no Testcontainers) verifying that <c>RoleRadar.Api</c> and
/// <c>RoleRadar.Identity</c> extract inbound W3C <c>traceparent</c>, emit spans whose 32-character
/// lowercase hex <c>TraceId</c> matches the RFC 9457 <c>traceId</c> and console logs, and scrub
/// query strings, headers, status descriptions, and raw exception strings via <see cref="RoleRadarSpanAttributeProcessor"/>.
/// </summary>
public sealed class DistributedTracingContractTests
{
    private const string ApiQueryTraceId = "0af7651916cd43dd8448eb211c803191"; // gitleaks:allow (fictional trace id test vector, not a credential)
    private const string ApiFaultTraceId = "0af7651916cd43dd8448eb211c803192"; // gitleaks:allow (fictional trace id test vector, not a credential)
    private const string IdentityTraceId = "0af7651916cd43dd8448eb211c803193";
    private const string ParentSpanId = "b7ad6b7169203331";

    private const string Subject = "0192e4a1-0000-7000-8000-0000000000aa";
    private const string Issuer = "https://test-identity.local";
    private const string Audience = "roleradar.api";
    private const string SigningSecret = "this-is-a-very-long-test-secret-key-for-tracing-32-bytes!!";
    private const string FaultHeader = "X-Test-Fault-Trace";

    private const string SentinelQueryCode = "SENTINEL-QUERY-CODE-999";
    private const string SentinelPkceVerifier = "SENTINEL-PKCE-VERIFIER-999";
    private const string SentinelOAuthState = "SENTINEL-OAUTH-STATE-999";
    private const string SentinelCookie = "SENTINEL-COOKIE-VALUE-999";
    private const string SentinelExceptionSecret = "SENTINEL-EX-PW-1000"; // gitleaks:allow (fictional test sentinel)

    private sealed class SynchronizedActivityList : ICollection<Activity>
    {
        private readonly List<Activity> _inner = [];
        private readonly object _gate = new();

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _inner.Count;
                }
            }
        }

        public bool IsReadOnly => false;

        public void Add(Activity item)
        {
            lock (_gate)
            {
                _inner.Add(item);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _inner.Clear();
            }
        }

        public bool Contains(Activity item)
        {
            lock (_gate)
            {
                return _inner.Contains(item);
            }
        }

        public void CopyTo(Activity[] array, int arrayIndex)
        {
            lock (_gate)
            {
                _inner.CopyTo(array, arrayIndex);
            }
        }

        public bool Remove(Activity item)
        {
            lock (_gate)
            {
                return _inner.Remove(item);
            }
        }

        public Activity[] SnapshotForTrace(string traceId)
        {
            lock (_gate)
            {
                return _inner.Where(s => s.TraceId.ToHexString() == traceId).ToArray();
            }
        }

        public async Task<Activity[]> WaitForSpansForTraceAsync(string traceId, int expectedCount = 1, TimeSpan? timeout = null)
        {
            var limit = timeout ?? TimeSpan.FromSeconds(5);
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < limit)
            {
                lock (_gate)
                {
                    var matching = _inner.Where(s => s.TraceId.ToHexString() == traceId).ToArray();
                    if (matching.Length >= expectedCount)
                    {
                        return matching;
                    }
                }

                await Task.Delay(25);
            }

            lock (_gate)
            {
                return _inner.Where(s => s.TraceId.ToHexString() == traceId).ToArray();
            }
        }

        public IEnumerator<Activity> GetEnumerator()
        {
            Activity[] copy;
            lock (_gate)
            {
                copy = [.. _inner];
            }

            return ((IEnumerable<Activity>)copy).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FaultOnHeaderHandler : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            if (context.Resource is Microsoft.AspNetCore.Http.HttpContext http
                && http.Request.Headers.ContainsKey(FaultHeader))
            {
                throw new InvalidOperationException($"fault {SentinelExceptionSecret} at db.fictional.invalid:27017");
            }

            return Task.CompletedTask;
        }
    }

    private static string CreateValidToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret));
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, Subject), new Claim("scope", Audience)],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<(WebApplication App, HttpClient Client, FormatterLogCapture Capture, SynchronizedActivityList Spans)> StartInProcessApiAsync()
    {
        var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("api", LogFormat.Json));
        var exportedSpans = new SynchronizedActivityList();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret));

        var app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Logging.AddProvider(capture);
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = "mongodb://127.0.0.1:1/?directConnection=true",
                    ["DatabaseName"] = "rrtest_contract_no_mongo",
                    ["Authentication:Authority"] = Issuer,
                    ["Authentication:Audience"] = Audience,
                    ["Owner:AllowedSubjects:0"] = Subject,
                });
                builder.Services.AddSingleton<IAuthorizationHandler, FaultOnHeaderHandler>();
                builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
                {
                    opts.Authority = string.Empty;
                    opts.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration { Issuer = Issuer });
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = Issuer,
                        ValidAudience = Audience,
                        IssuerSigningKey = key,
                    };
                });
                builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddInMemoryExporter(exportedSpans));
            });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var client = new HttpClient { BaseAddress = new Uri(address) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateValidToken());
        return (app, client, capture, exportedSpans);
    }

    private static string SerializeSpans(IEnumerable<Activity> spans)
    {
        var sb = new StringBuilder();
        foreach (var span in spans)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Span: '{span.DisplayName}' Source='{span.Source.Name}' TraceId={span.TraceId} SpanId={span.SpanId} Status={span.Status} StatusDesc={span.StatusDescription}");
            foreach (var tag in span.TagObjects)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Tag[{tag.Key}]={tag.Value}");
            }

            foreach (var ev in span.Events)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Event[{ev.Name}]:");
                foreach (var attr in ev.Tags)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    Attr[{attr.Key}]={attr.Value}");
                }
            }
        }

        return sb.ToString();
    }

    [Fact]
    public async Task Api_ExtractsInboundTraceparent_MatchesProblemDetailsAndConsoleLogs_AndScrubsQueryAndHeaders()
    {
        var (app, client, capture, spans) = await StartInProcessApiAsync();
        try
        {
            var token = CreateValidToken();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/insights/stats?window=nonsense&code={SentinelQueryCode}&code_verifier={SentinelPkceVerifier}");
            request.Headers.TryAddWithoutValidation("traceparent", $"00-{ApiQueryTraceId}-{ParentSpanId}-01");
            request.Headers.TryAddWithoutValidation("Cookie", $"__Host-roleradar.sid={SentinelCookie}");

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(ApiQueryTraceId, problem.GetProperty("traceId").GetString());

            var logRecords = capture.Lines
                .Select(l => JsonDocument.Parse(l).RootElement.Clone())
                .Where(r => r.TryGetProperty("event", out var e) && e.GetString() == "RequestResponseLog")
                .ToList();
            Assert.Contains(logRecords, r => r.GetProperty("traceId").GetString() == ApiQueryTraceId);

            app.Services.GetRequiredService<TracerProvider>().ForceFlush();
            var traceSpans = await spans.WaitForSpansForTraceAsync(ApiQueryTraceId);

            var serverSpan = Assert.Single(traceSpans, s =>
                string.Equals(s.GetTagItem("url.path") as string, "/api/v1/insights/stats", StringComparison.Ordinal));

            Assert.Equal(ParentSpanId, serverSpan.ParentSpanId.ToHexString());
            Assert.Equal("GET", serverSpan.GetTagItem("http.request.method"));
            Assert.Equal(400, Convert.ToInt32(serverSpan.GetTagItem("http.response.status_code"), CultureInfo.InvariantCulture));
            Assert.Null(serverSpan.GetTagItem("url.query"));

            var serialized = SerializeSpans(traceSpans);
            Assert.DoesNotContain(SentinelQueryCode, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelPkceVerifier, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelCookie, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(token, serialized, StringComparison.Ordinal);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Api_UnhandledFault_EmitsErrorSpanWithMatchingTraceId_AndStripsExceptionMessageAndStacktrace()
    {
        var (app, client, _, spans) = await StartInProcessApiAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/postings/{Guid.CreateVersion7()}");
            request.Headers.TryAddWithoutValidation("traceparent", $"00-{ApiFaultTraceId}-{ParentSpanId}-01");
            request.Headers.Add(FaultHeader, "1");

            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(ApiFaultTraceId, problem.GetProperty("traceId").GetString());

            app.Services.GetRequiredService<TracerProvider>().ForceFlush();
            var traceSpans = await spans.WaitForSpansForTraceAsync(ApiFaultTraceId);

            var serverSpan = Assert.Single(traceSpans);
            Assert.Equal(500, Convert.ToInt32(serverSpan.GetTagItem("http.response.status_code"), CultureInfo.InvariantCulture));
            Assert.True(string.IsNullOrEmpty(serverSpan.StatusDescription));

            var serialized = SerializeSpans(traceSpans);
            Assert.DoesNotContain(SentinelExceptionSecret, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("db.fictional.invalid", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("exception.message", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("exception.stacktrace", serialized, StringComparison.Ordinal);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Identity_ExtractsInboundTraceparent_MatchesConsoleLogs_AndScrubsOAuthQueryAndCookies()
    {
        var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("identity", LogFormat.Json));
        var spans = new SynchronizedActivityList();
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(builder =>
        {
            builder.Logging.AddProvider(capture);
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddInMemoryExporter(spans));
        });

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/connect/authorize?client_id=roleradar-web&response_type=code&scope=openid&redirect_uri=https%3A%2F%2Flocalhost%3A5003%2Fauth%2Fcallback&code_challenge={SentinelPkceVerifier}&code_challenge_method=S256&state={SentinelOAuthState}");
            request.Headers.TryAddWithoutValidation("traceparent", $"00-{IdentityTraceId}-{ParentSpanId}-01");
            request.Headers.TryAddWithoutValidation("Cookie", $"__Host-roleradar.identity={SentinelCookie}");

            using var response = await client.SendAsync(request);

            var logRecords = capture.Lines
                .Select(l => JsonDocument.Parse(l).RootElement.Clone())
                .Where(r => r.TryGetProperty("event", out var e) && e.GetString() == "RequestResponseLog")
                .ToList();
            Assert.Contains(logRecords, r => r.GetProperty("traceId").GetString() == IdentityTraceId);

            app.Services.GetRequiredService<TracerProvider>().ForceFlush();
            var traceSpans = await spans.WaitForSpansForTraceAsync(IdentityTraceId);

            var authorizeSpan = Assert.Single(traceSpans, s =>
                string.Equals(s.GetTagItem("url.path") as string, "/connect/authorize", StringComparison.Ordinal));

            Assert.Equal(ParentSpanId, authorizeSpan.ParentSpanId.ToHexString());
            Assert.Equal("GET", authorizeSpan.GetTagItem("http.request.method"));
            Assert.Null(authorizeSpan.GetTagItem("url.query"));

            var serialized = SerializeSpans(traceSpans);
            Assert.DoesNotContain(SentinelPkceVerifier, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelOAuthState, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelCookie, serialized, StringComparison.Ordinal);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            try
            {
                Directory.Delete(dbDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task NativeAspNetCoreSpan_ExaminesBuiltInTags_ForSuccessAndFault()
    {
        // 1. Verify what a raw WebApplication with Microsoft.AspNetCore ActivitySource alone produces (no RequestCompletedMiddleware)
        var rawSpans = new SynchronizedActivityList();
        var rawBuilder = WebApplication.CreateBuilder(["--urls", "http://127.0.0.1:0"]);
        rawBuilder.Services.AddRouting();
        rawBuilder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource("Microsoft.AspNetCore")
                .AddInMemoryExporter(rawSpans));

        var rawApp = rawBuilder.Build();
        rawApp.MapGet("/raw/test", () => Microsoft.AspNetCore.Http.Results.Ok());
        await rawApp.StartAsync();

        try
        {
            const string rawTraceId = "99999999999999999999999999999999";
            var rawAddress = rawApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var rawClient = new HttpClient { BaseAddress = new Uri(rawAddress) };
            using var rawReq = new HttpRequestMessage(HttpMethod.Get, "/raw/test");
            rawReq.Headers.TryAddWithoutValidation("traceparent", $"00-{rawTraceId}-1111111111111111-01");
            using var rawResp = await rawClient.SendAsync(rawReq);
            Assert.Equal(HttpStatusCode.OK, rawResp.StatusCode);

            rawApp.Services.GetRequiredService<TracerProvider>().ForceFlush();
            var rawTraceSpans = await rawSpans.WaitForSpansForTraceAsync(rawTraceId);
            var rawSpan = Assert.Single(rawTraceSpans);

            // Document RR-90 finding: native Microsoft.AspNetCore hosting activity lacks HTTP semantic convention tags.
            // When isolated before any OpenTelemetry.Instrumentation.AspNetCore registration in the AppDomain, Tags is empty ([]).
            // When run after OpenTelemetry.Instrumentation.AspNetCore has registered its DiagnosticSource listener in the AppDomain,
            // the instrumentation listener populates the semantic convention tags.
            Assert.True(rawSpan.DisplayName is "Microsoft.AspNetCore.Hosting.HttpRequestIn" or "GET /raw/test");
            Assert.Equal("Microsoft.AspNetCore.Hosting.HttpRequestIn", rawSpan.OperationName);
            Assert.True(rawSpan.GetTagItem("http.request.method") is null or "GET");
            Assert.True(rawSpan.GetTagItem("url.scheme") is null or "http");
            Assert.True(rawSpan.GetTagItem("url.path") is null or "/raw/test");
            Assert.True(rawSpan.GetTagItem("http.response.status_code") is null or 200);
            Assert.True(rawSpan.GetTagItem("http.route") is null or "/raw/test");
            Assert.Equal(ActivityStatusCode.Unset, rawSpan.Status);
        }
        finally
        {
            await rawApp.StopAsync();
            await rawApp.DisposeAsync();
        }

        // 2. Verify that in RoleRadar.Api and RoleRadar.Identity, OpenTelemetry.Instrumentation.AspNetCore
        // populates http.request.method, url.scheme, url.path, http.response.status_code, http.route,
        // route-based display name, and Error status on 5xx.
        var (app, client, _, spans) = await StartInProcessApiAsync();
        try
        {
            const string okTraceId = "55555555555555555555555555555555";
            const string faultTraceId = "77777777777777777777777777777777";

            using var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/insights/stats");
            req1.Headers.TryAddWithoutValidation("traceparent", $"00-{okTraceId}-6666666666666666-01");
            using var resp1 = await client.SendAsync(req1);

            using var req2 = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/postings/{Guid.CreateVersion7()}");
            req2.Headers.TryAddWithoutValidation("traceparent", $"00-{faultTraceId}-8888888888888888-01");
            req2.Headers.Add(FaultHeader, "1");
            using var resp2 = await client.SendAsync(req2);

            app.Services.GetRequiredService<TracerProvider>().ForceFlush();

            var okSpans = await spans.WaitForSpansForTraceAsync(okTraceId);
            var okServerSpan = Assert.Single(okSpans, s => s.Source.Name == "Microsoft.AspNetCore");
            Assert.Equal("GET /api/v1/insights/stats", okServerSpan.DisplayName);
            Assert.Equal("GET", okServerSpan.GetTagItem("http.request.method"));
            Assert.Equal("http", okServerSpan.GetTagItem("url.scheme"));
            Assert.Equal("/api/v1/insights/stats", okServerSpan.GetTagItem("url.path"));
            Assert.Equal(400, Convert.ToInt32(okServerSpan.GetTagItem("http.response.status_code"), CultureInfo.InvariantCulture));
            Assert.Equal("/api/v1/insights/stats", okServerSpan.GetTagItem("http.route"));

            var faultSpans = await spans.WaitForSpansForTraceAsync(faultTraceId);
            var faultServerSpan = Assert.Single(faultSpans, s => s.Source.Name == "Microsoft.AspNetCore");
            Assert.Equal(500, Convert.ToInt32(faultServerSpan.GetTagItem("http.response.status_code"), CultureInfo.InvariantCulture));
            Assert.Equal(ActivityStatusCode.Error, faultServerSpan.Status);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }

        // 3. Verify Identity host carries http.request.method, url.scheme, url.path, http.response.status_code, and route
        var identitySpans = new SynchronizedActivityList();
        var (identityApp, identityClient, _, identityDbDir) = await IdentityContractTestServer.StartHostAsync(builder =>
        {
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddInMemoryExporter(identitySpans));
        });

        try
        {
            const string idTraceId = "66666666666666666666666666666666";
            using var idReq = new HttpRequestMessage(HttpMethod.Get, "/.well-known/openid-configuration");
            idReq.Headers.TryAddWithoutValidation("traceparent", $"00-{idTraceId}-2222222222222222-01");
            using var idResp = await identityClient.SendAsync(idReq);
            Assert.Equal(HttpStatusCode.OK, idResp.StatusCode);

            identityApp.Services.GetRequiredService<TracerProvider>().ForceFlush();
            var idSpans = await identitySpans.WaitForSpansForTraceAsync(idTraceId);
            var idServerSpan = Assert.Single(idSpans, s => s.Source.Name == "Microsoft.AspNetCore");
            Assert.Equal("GET", idServerSpan.GetTagItem("http.request.method"));
            Assert.Equal("http", idServerSpan.GetTagItem("url.scheme"));
            Assert.Equal("/.well-known/openid-configuration", idServerSpan.GetTagItem("url.path"));
            Assert.Equal(200, Convert.ToInt32(idServerSpan.GetTagItem("http.response.status_code"), CultureInfo.InvariantCulture));
        }
        finally
        {
            identityClient.Dispose();
            await identityApp.StopAsync();
            await identityApp.DisposeAsync();
            try
            {
                Directory.Delete(identityDbDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Api_RequestWithSentinelsInQueryUserAgentAndHeaders_ExportsSpanWithNoneOfThem()
    {
        const string sc002TraceId = "88888888888888888888888888888888";
        const string sentinelQuerySecret = "SENTINEL-SC002-QUERY-SECRET-12345"; // gitleaks:allow (fictional test vector, not a credential)
        const string sentinelUaSecret = "SENTINEL-SC002-USER-AGENT-SECRET-67890"; // gitleaks:allow (fictional test vector, not a credential)
        const string sentinelHeaderSecret = "SENTINEL-SC002-CUSTOM-HEADER-SECRET-99999"; // gitleaks:allow (fictional test vector, not a credential)

        var (app, client, _, spans) = await StartInProcessApiAsync();
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/insights/stats?secret={sentinelQuerySecret}&token=SENTINEL-SC002-QUERY-TOKEN"); // gitleaks:allow (fictional test vector, not a credential)
            request.Headers.TryAddWithoutValidation("traceparent", $"00-{sc002TraceId}-{ParentSpanId}-01");
            request.Headers.TryAddWithoutValidation("User-Agent", sentinelUaSecret);
            request.Headers.TryAddWithoutValidation("X-Custom-Secret-Header", sentinelHeaderSecret);

            using var response = await client.SendAsync(request);

            app.Services.GetRequiredService<TracerProvider>().ForceFlush();
            var exportedSpans = await spans.WaitForSpansForTraceAsync(sc002TraceId);
            var serverSpan = Assert.Single(exportedSpans, s => s.Source.Name == "Microsoft.AspNetCore");

            // SC-002: url.query, user_agent.original, client.address, and custom headers are removed by RoleRadarSpanAttributeProcessor
            Assert.Null(serverSpan.GetTagItem("url.query"));
            Assert.Null(serverSpan.GetTagItem("user_agent.original"));
            Assert.Null(serverSpan.GetTagItem("client.address"));
            Assert.DoesNotContain(serverSpan.TagObjects, t => t.Key.StartsWith("http.request.header.", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(serverSpan.TagObjects, t => t.Key.StartsWith("http.response.header.", StringComparison.OrdinalIgnoreCase));

            var serialized = SerializeSpans(exportedSpans);
            Assert.DoesNotContain(sentinelQuerySecret, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinelUaSecret, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinelHeaderSecret, serialized, StringComparison.Ordinal);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
