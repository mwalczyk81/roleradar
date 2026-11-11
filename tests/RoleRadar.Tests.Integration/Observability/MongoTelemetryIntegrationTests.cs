using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;
using OpenTelemetry.Trace;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Observability;

/// <summary>
/// Feature 015 US1 &amp; US2 (FR-002, FR-005, FR-012, SC-001, SC-002, ADR 0050):
/// Testcontainers MongoDB integration tests verifying that <c>RoleRadar.Api</c> emits child
/// <c>MongoDB.Driver</c> spans (<c>insert</c>, <c>find</c>, <c>update</c>, <c>commitTransaction</c>)
/// under the parent API HTTP span with matching 32-character hex <c>TraceId</c> equal to
/// <c>request_correlation_id</c> in <c>audit_log</c>, and that <c>TracingOptions.QueryTextMaxLength == 0</c>
/// plus <c>RoleRadarSpanAttributeProcessor</c> guarantee zero <c>db.query.text</c> or sentinel payload leakage.
/// </summary>
[Trait("Lane", "default")]
public sealed class MongoTelemetryIntegrationTests(SharedMongoFixture mongo)
{
    private const string WriteTraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string ReadTraceId = "11112222333344445555666677778888";
    private const string ParentSpanId = "b7ad6b7169203331";

    private const string Subject = "0192e4a1-0000-7000-8000-0000000000aa";
    private const string Issuer = "https://test-identity.local";
    private const string Audience = "roleradar.api";
    private const string SigningSecret = "this-is-a-very-long-test-secret-key-for-mongo-telemetry-32b!";

    private const string SentinelCompanyDesc = "SENTINEL-COMPANY-DESC-1002";
    private const string SentinelPostingTitle = "SENTINEL-POSTING-TITLE-1003";
    private const string SentinelTransitionReason = "SENTINEL-TRANSITION-REASON-1004";
    private const string SentinelFilterLiteral = "sentinel-filter-literal-1005";

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

        public Activity[] SnapshotForTraces(params string[] traceIds)
        {
            var set = traceIds.ToHashSet(StringComparer.Ordinal);
            lock (_gate)
            {
                return _inner.Where(s => set.Contains(s.TraceId.ToHexString())).ToArray();
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

    private static async Task<(WebApplication App, HttpClient Client, SynchronizedActivityList Spans)> StartApiAsync(string connectionString, string databaseName)
    {
        var exportedSpans = new SynchronizedActivityList();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret));

        var app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = connectionString,
                    ["DatabaseName"] = databaseName,
                    ["Authentication:Authority"] = Issuer,
                    ["Authentication:Audience"] = Audience,
                    ["Owner:AllowedSubjects:0"] = Subject,
                });
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
        return (app, client, exportedSpans);
    }

    private static string SerializeSpans(IEnumerable<Activity> spans)
    {
        var sb = new StringBuilder();
        foreach (var span in spans)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Span: {span.DisplayName} Source={span.Source.Name} TraceId={span.TraceId} SpanId={span.SpanId} ParentSpanId={span.ParentSpanId} StatusDesc={span.StatusDescription}");
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
    public void ConnectionStringRedactor_CreateClient_EnablesTracing_WithQueryTextMaxLengthZero()
    {
        using var client = ConnectionStringRedactor.CreateClient(mongo.ConnectionString, TimeSpan.FromSeconds(5));
        Assert.NotNull(client.Settings.TracingOptions);
        Assert.False(client.Settings.TracingOptions.Disabled);
        Assert.Equal(0, client.Settings.TracingOptions.QueryTextMaxLength);
    }

    [Fact]
    public async Task Api_RequestsAgainstMongo_EmitChildMongoSpans_WithMatchingTraceIdAndAuditCorrelation_AndZeroQueryTextOrSentinelLeakage()
    {
        var dbName = mongo.CreateDatabaseName("rrtest_mongo_telemetry");
        var (app, client, spans) = await StartApiAsync(mongo.ConnectionString, dbName);
        try
        {
            Guid postingId;
            string versionToken;
            using (var scope = app.Services.CreateScope())
            {
                var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
                var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

                var company = await companyHandler.HandleAsync(new RecordCompanyCommand(
                    Name: "Vanguard Fictional Systems",
                    Description: SentinelCompanyDesc,
                    Actor: "system"));
                Assert.True(company.IsSuccess);

                var posting = await postingHandler.HandleAsync(new RecordPostingCommand(
                    CompanyId: company.Value!.Company.Id,
                    Title: SentinelPostingTitle,
                    RequisitionId: "VFS-015",
                    AtsTenant: "vanguard",
                    Actor: "mcp-stdio"));
                Assert.True(posting.IsSuccess);

                postingId = posting.Value!.Posting!.Id;
                versionToken = posting.Value.Posting.VersionToken.ToString();
            }

            spans.Clear();

            // 1. Transactional write command (find, update, insert into audit_log, commitTransaction)
            using (var writeReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/postings/{postingId}/transition")
            {
                Content = JsonContent.Create(new
                {
                    toDisposition = "screened_out",
                    reason = SentinelTransitionReason,
                    versionToken
                })
            })
            {
                writeReq.Headers.TryAddWithoutValidation("traceparent", $"00-{WriteTraceId}-{ParentSpanId}-01");
                using var writeResp = await client.SendAsync(writeReq);
                Assert.Equal(HttpStatusCode.OK, writeResp.StatusCode);
            }

            // 2. OData filter read command (find with filter literal)
            using (var readReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/odata/Companies?$filter=contains(tolower(name),'{SentinelFilterLiteral}')"))
            {
                readReq.Headers.TryAddWithoutValidation("traceparent", $"00-{ReadTraceId}-{ParentSpanId}-01");
                using var readResp = await client.SendAsync(readReq);
                Assert.Equal(HttpStatusCode.OK, readResp.StatusCode);
            }

            app.Services.GetRequiredService<TracerProvider>().ForceFlush();
            var relevantSpans = spans.SnapshotForTraces(WriteTraceId, ReadTraceId);

            // Verify parent HTTP span and child MongoDB.Driver spans for the write trace
            var writeSpans = relevantSpans.Where(s => s.TraceId.ToHexString() == WriteTraceId).ToList();
            var apiServerSpan = Assert.Single(writeSpans, s =>
                string.Equals(s.GetTagItem("url.path") as string, $"/api/v1/postings/{postingId}/transition", StringComparison.Ordinal));
            Assert.Equal(ParentSpanId, apiServerSpan.ParentSpanId.ToHexString());

            var mongoWriteSpans = writeSpans.Where(s =>
                s.Source.Name.StartsWith("MongoDB", StringComparison.Ordinal)
                || string.Equals(s.GetTagItem("db.system") as string, "mongodb", StringComparison.Ordinal)
                || string.Equals(s.GetTagItem("db.system.name") as string, "mongodb", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(mongoWriteSpans);

            var commandNames = mongoWriteSpans
                .Select(s => (s.GetTagItem("db.command.name") ?? s.GetTagItem("db.operation") ?? s.GetTagItem("db.operation.name") ?? s.DisplayName)?.ToString() ?? string.Empty)
                .ToList();
            Assert.Contains(commandNames, c => c.Contains("find", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(commandNames, c => c.Contains("update", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(commandNames, c => c.Contains("insert", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(commandNames, c => c.Contains("commitTransaction", StringComparison.OrdinalIgnoreCase));

            // Every Mongo span on the write trace must be a descendant of the API server span
            var writeSpanIds = writeSpans.Select(s => s.SpanId).ToHashSet();
            Assert.All(mongoWriteSpans, m => Assert.Contains(m.ParentSpanId, writeSpanIds));

            // Verify audit_log correlation matches the exact same 32-hex traceId (SC-001)
            using var verifyClient = new MongoClient(mongo.ConnectionString);
            var audit = verifyClient.GetDatabase(dbName).GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
            var entry = await audit.Find(Builders<BsonDocument>.Filter.Eq("request_correlation_id", WriteTraceId)).FirstOrDefaultAsync();
            Assert.NotNull(entry);

            // Verify read trace also emitted MongoDB find span without query text or filter literal
            var readSpans = relevantSpans.Where(s => s.TraceId.ToHexString() == ReadTraceId).ToList();
            Assert.Contains(readSpans, s => s.Source.Name.StartsWith("MongoDB", StringComparison.Ordinal));

            // Verify db.query.text and db.statement are absent on all spans, and no sentinel values leaked (SC-002)
            Assert.All(relevantSpans, s =>
            {
                Assert.Null(s.GetTagItem("db.query.text"));
                Assert.Null(s.GetTagItem("db.statement"));
                Assert.Null(s.GetTagItem("url.query"));
            });

            var serialized = SerializeSpans(relevantSpans);
            Assert.DoesNotContain(SentinelCompanyDesc, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelPostingTitle, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelTransitionReason, serialized, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelFilterLiteral, serialized, StringComparison.Ordinal);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
