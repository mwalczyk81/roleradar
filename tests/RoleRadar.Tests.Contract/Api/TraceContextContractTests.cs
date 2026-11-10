using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Observability;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

/// <summary>
/// Feature 014 US2 (FR-023 to FR-027, contracts/trace-context.md, ADR 0044), replacing the 013 correlation tests: the id of a
/// request is the bare W3C trace id. A valid inbound <c>traceparent</c> is honoured and reaches the log records, the problem
/// document and the audit entry of a write; a malformed, all-zero or upper-case one is replaced and never logged;
/// <c>X-Correlation-ID</c> is not read and not written; no response header returns the trace id. Logs are captured through the
/// production formatter.
/// </summary>
public sealed class TraceContextContractTests(SharedMongoFixture sharedMongo)
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string Traceparent = "00-" + TraceId + "-b7ad6b7169203331-01";

    private static readonly Regex HexTraceId = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    private async Task<(ApiContractTestHost Host, FormatterLogCapture Capture)> StartAsync(string prefix, bool debugApplication = false)
    {
        var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("api", LogFormat.Json));
        var host = await ApiContractTestHost.StartAsync(_sharedMongo, prefix, builder =>
        {
            builder.Logging.AddProvider(capture);
            if (debugApplication)
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Logging:LogLevel:RoleRadar.Application"] = "Debug"
                });
            }
        });
        return (host, capture);
    }

    private static List<JsonElement> Records(FormatterLogCapture capture) =>
        capture.Lines.Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();

    private static List<JsonElement> RequestCompleted(FormatterLogCapture capture, string traceId) =>
        Records(capture)
            .Where(r => r.TryGetProperty("event", out var e) && e.GetString() == "RequestCompleted")
            .Where(r => r.TryGetProperty("traceId", out var c) && c.GetString() == traceId)
            .ToList();

    private static HttpRequestMessage Get(string url, string? traceparent = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (traceparent is not null)
        {
            request.Headers.TryAddWithoutValidation("traceparent", traceparent);
        }

        return request;
    }

    private static async Task<(Guid PostingId, string VersionToken)> SeedPostingAsync(ApiContractTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();
        var company = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: "Lumen Fictional Labs", Actor: "system"));
        var posting = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: company.Value!.Company.Id,
            Title: "Staff Platform Engineer",
            RequisitionId: "LFL-014",
            AtsTenant: "lumen",
            Actor: "mcp-stdio"));
        return (posting.Value!.Posting!.Id, posting.Value.Posting.VersionToken.ToString());
    }

    [Fact]
    public async Task AValidInboundTraceparent_GivesItsTraceId_ToTheLogRecord_AndTheProblemDocument()
    {
        var (host, capture) = await StartAsync("rrtest_api_trace_valid");
        await using var hostScope = host;

        using var response = await host.Client.SendAsync(Get("/odata/Postings?$orderby=content", Traceparent));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(TraceId, body.GetProperty("traceId").GetString());
        Assert.NotEmpty(capture.Lines);
        Assert.NotEmpty(RequestCompleted(capture, TraceId));
    }

    [Theory]
    [InlineData("not-a-traceparent")]
    [InlineData("00-00000000000000000000000000000000-b7ad6b7169203331-01")]
    [InlineData("00-0AF7651916CD43DD8448EB211C80319C-b7ad6b7169203331-01")]
    [InlineData("ff-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01")]
    public async Task AMalformedAllZeroOrUpperCaseTraceparent_IsReplaced_AndNeverLogged(string bad)
    {
        var (host, capture) = await StartAsync("rrtest_api_trace_bad");
        await using var hostScope = host;

        using var response = await host.Client.SendAsync(Get("/odata/Postings?$orderby=content", bad));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var replaced = body.GetProperty("traceId").GetString();

        Assert.Matches(HexTraceId, replaced!);
        Assert.NotEqual(new string('0', 32), replaced);
        Assert.NotEqual(TraceId, replaced);
        Assert.NotEmpty(capture.Lines);
        Assert.NotEmpty(RequestCompleted(capture, replaced!));
        Assert.DoesNotContain(bad, capture.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("0AF7651916CD43DD8448EB211C80319C", capture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequestWithNoTraceContext_StillGetsA32HexId()
    {
        var (host, capture) = await StartAsync("rrtest_api_trace_none");
        await using var hostScope = host;

        using var response = await host.Client.SendAsync(Get("/odata/Postings?$orderby=content"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var id = body.GetProperty("traceId").GetString();
        Assert.Matches(HexTraceId, id!);
        Assert.NotEmpty(capture.Lines);
        Assert.NotEmpty(RequestCompleted(capture, id!));
    }

    [Fact]
    public async Task NoResponseHeader_ReturnsTheTraceId_AndXCorrelationIdIsIgnored()
    {
        var (host, capture) = await StartAsync("rrtest_api_trace_headers");
        await using var hostScope = host;

        using var request = Get("/odata/Postings", Traceparent);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", "legacy-corr-ignored-0001");
        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Correlation-ID"), "X-Correlation-ID must not be returned.");
        Assert.DoesNotContain(response.Headers, h => h.Value.Any(v => v.Contains(TraceId, StringComparison.OrdinalIgnoreCase)));
        Assert.NotEmpty(capture.Lines);
        Assert.DoesNotContain("legacy-corr-ignored-0001", capture.Text, StringComparison.Ordinal);
        Assert.Single(RequestCompleted(capture, TraceId));
    }

    [Fact]
    public async Task EveryLogRecordWrittenDuringARequest_HasTheFieldTraceId_AndNoCorrelationId()
    {
        var (host, capture) = await StartAsync("rrtest_api_trace_field");
        await using var hostScope = host;

        using var response = await host.Client.SendAsync(Get("/odata/Postings", Traceparent));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var records = Records(capture);
        Assert.NotEmpty(records);
        Assert.NotEmpty(RequestCompleted(capture, TraceId));
        Assert.DoesNotContain(records, r => r.TryGetProperty("correlationId", out _));
    }

    [Fact]
    public async Task EachRequest_WritesExactlyOneRequestCompletedRecord_WithoutTheQueryString()
    {
        var (host, capture) = await StartAsync("rrtest_api_trace_request_record");
        await using var hostScope = host;

        using var response = await host.Client.SendAsync(Get("/odata/Postings?$orderby=content&$top=5", Traceparent));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.NotEmpty(capture.Lines);
        var record = Assert.Single(RequestCompleted(capture, TraceId));
        var props = record.GetProperty("props");
        Assert.Equal("GET", props.GetProperty("method").GetString());
        Assert.Equal("/odata/Postings", props.GetProperty("path").GetString());
        Assert.Equal(400, props.GetProperty("status").GetInt32());
        Assert.True(props.GetProperty("durationMs").GetDouble() >= 0);
        Assert.DoesNotContain("orderby", capture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthRequests_LogAtDebug_SoNoRequestCompletedRecordAppears()
    {
        var (host, capture) = await StartAsync("rrtest_api_trace_health");
        await using var hostScope = host;

        using var anonymous = new HttpClient { BaseAddress = host.Client.BaseAddress };
        using var response = await anonymous.SendAsync(Get("/health", Traceparent));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Non-vacuous: a normal request records a line, the health request does not.
        using var normal = await host.Client.SendAsync(Get("/odata/Postings", "00-1111111111111111111111111111aaaa-b7ad6b7169203331-01"));
        Assert.NotEmpty(RequestCompleted(capture, "1111111111111111111111111111aaaa"));
        Assert.Empty(RequestCompleted(capture, TraceId));
    }

    [Fact]
    public async Task CommandRecords_AndTheAuditEntry_WrittenDuringTheRequest_CarryItsTraceId()
    {
        var (host, capture) = await StartAsync("rrtest_api_trace_command");
        await using var hostScope = host;
        var (postingId, versionToken) = await SeedPostingAsync(host);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/postings/{postingId}/transition")
        {
            Content = JsonContent.Create(new { toDisposition = "screened_out", reason = "Fictional reason", versionToken })
        };
        request.Headers.TryAddWithoutValidation("traceparent", Traceparent);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.NotEmpty(capture.Lines);
        Assert.NotEmpty(RequestCompleted(capture, TraceId));
        var commandStarting = Records(capture)
            .Where(r => r.TryGetProperty("eventId", out var e) && e.GetInt32() == 1001)
            .Where(r => r.GetProperty("msg").GetString()!.Contains("TransitionPosting", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(commandStarting);
        Assert.All(commandStarting, r => Assert.Equal(TraceId, r.GetProperty("traceId").GetString()));

        using var mongo = new MongoClient(_sharedMongo.ConnectionString);
        var audit = mongo.GetDatabase(host.DatabaseName).GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var entry = await audit.Find(Builders<BsonDocument>.Filter.Eq("request_correlation_id", TraceId)).FirstOrDefaultAsync();
        Assert.NotNull(entry);
    }

    [Fact]
    public async Task QueryRecords_WrittenDuringTheRequest_CarryItsTraceId_WhenDebugIsOn()
    {
        var (host, capture) = await StartAsync("rrtest_api_trace_query", debugApplication: true);
        await using var hostScope = host;

        using var response = await host.Client.SendAsync(Get($"/api/v1/notes/{Guid.CreateVersion7()}", Traceparent));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.NotEmpty(capture.Lines);
        Assert.NotEmpty(RequestCompleted(capture, TraceId));
        var queryStarting = Records(capture)
            .Where(r => r.TryGetProperty("eventId", out var e) && e.GetInt32() == 2001)
            .ToList();
        Assert.NotEmpty(queryStarting);
        Assert.All(queryStarting, r => Assert.Equal(TraceId, r.GetProperty("traceId").GetString()));
    }
}
