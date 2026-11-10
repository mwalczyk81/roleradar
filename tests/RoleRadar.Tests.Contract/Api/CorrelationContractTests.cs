using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Observability;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

/// <summary>
/// Feature 013 US1 (FR-004 to FR-007): every API response carries X-Correlation-ID, every envelope carries the same
/// id, and every record written during the request carries it. Logs are captured through the production formatter.
/// </summary>
public sealed class CorrelationContractTests(SharedMongoFixture sharedMongo)
{
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

    private static List<JsonElement> RequestCompleted(FormatterLogCapture capture, string correlationId) =>
        Records(capture)
            .Where(r => r.TryGetProperty("event", out var e) && e.GetString() == "RequestCompleted")
            .Where(r => r.TryGetProperty("correlationId", out var c) && c.GetString() == correlationId)
            .ToList();

    private static string HeaderOf(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues(CorrelationId.HeaderName, out var values), $"Missing {CorrelationId.HeaderName} on {(int)response.StatusCode}.");
        var id = Assert.Single(values);
        Assert.True(CorrelationId.TryAccept(id, out _), $"Header value '{id}' is not a well-formed correlation id.");
        return id;
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
            RequisitionId: "LFL-013",
            AtsTenant: "lumen",
            Actor: "mcp-stdio"));
        return (posting.Value!.Posting!.Id, posting.Value.Posting.VersionToken.ToString());
    }

    [Fact]
    public async Task EveryResponse_CarriesTheHeader()
    {
        var (host, capture) = await StartAsync("rrtest_api_corr_header");
        await using var _ = host;

        using var anonymous = new HttpClient { BaseAddress = host.Client.BaseAddress };
        var responses = new List<HttpResponseMessage>
        {
            await anonymous.GetAsync("/health"),
            await host.Client.GetAsync("/odata/Postings"),
            await host.Client.GetAsync($"/api/v1/postings/{Guid.CreateVersion7()}"),
            await anonymous.GetAsync("/odata/Postings"),
            await host.Client.GetAsync("/odata/Postings?$orderby=content"),
            await host.Client.PostAsJsonAsync($"/api/v1/postings/{Guid.CreateVersion7()}/transition", new { toDisposition = "not-a-disposition", versionToken = "x" }),
        };

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.NotFound);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.BadRequest);

        var ids = responses.Select(HeaderOf).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        // Non-vacuous capture first: at least one line, and a RequestCompleted line for the OData request (/health logs at Debug).
        Assert.NotEmpty(capture.Lines);
        Assert.NotEmpty(RequestCompleted(capture, ids[1]));
        Assert.Empty(RequestCompleted(capture, ids[0]));
    }

    [Fact]
    public async Task WellFormedInboundId_IsEchoed_AndMalformedIdIsReplacedAndNeverLogged()
    {
        var (host, capture) = await StartAsync("rrtest_api_corr_inbound");
        await using var _ = host;

        using var good = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings");
        good.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "test-corr-0001");
        var goodResponse = await host.Client.SendAsync(good);
        Assert.Equal("test-corr-0001", HeaderOf(goodResponse));

        const string malformed = "bad id<script>";
        using var bad = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings");
        bad.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, malformed);
        var badResponse = await host.Client.SendAsync(bad);
        var replaced = HeaderOf(badResponse);

        Assert.NotEqual(malformed, replaced);
        Assert.NotEmpty(capture.Lines);
        Assert.NotEmpty(RequestCompleted(capture, replaced));
        Assert.DoesNotContain("bad id", capture.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", capture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryErrorEnvelope_CarriesTheHeaderId()
    {
        var (host, _) = await StartAsync("rrtest_api_corr_envelope");
        await using var __ = host;

        var failing = new[]
        {
            await host.Client.GetAsync($"/api/v1/postings/{Guid.CreateVersion7()}"),
            await host.Client.GetAsync("/odata/Postings?$orderby=content"),
            await host.Client.PostAsJsonAsync($"/api/v1/postings/{Guid.CreateVersion7()}/transition", new { toDisposition = "not-a-disposition", versionToken = "x" }),
        };

        foreach (var response in failing)
        {
            var header = HeaderOf(response);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(body.GetProperty("ok").GetBoolean());
            Assert.True(body.TryGetProperty("correlationId", out var envelopeId), $"Envelope for {(int)response.StatusCode} has no correlationId.");
            Assert.Equal(header, envelopeId.GetString());
        }
    }

    [Fact]
    public async Task EachRequest_WritesExactlyOneRequestCompletedRecord_WithoutTheQueryString()
    {
        var (host, capture) = await StartAsync("rrtest_api_corr_request_record");
        await using var _ = host;

        var response = await host.Client.GetAsync("/odata/Postings?$orderby=content&$top=5");
        var id = HeaderOf(response);

        Assert.NotEmpty(capture.Lines);
        var record = Assert.Single(RequestCompleted(capture, id));
        var props = record.GetProperty("props");
        Assert.Equal("GET", props.GetProperty("method").GetString());
        Assert.Equal("/odata/Postings", props.GetProperty("path").GetString());
        Assert.Equal(400, props.GetProperty("status").GetInt32());
        Assert.True(props.GetProperty("durationMs").GetDouble() >= 0);
        Assert.DoesNotContain("orderby", capture.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommandRecords_WrittenDuringTheRequest_CarryItsCorrelationId()
    {
        var (host, capture) = await StartAsync("rrtest_api_corr_command");
        await using var _ = host;
        var (postingId, versionToken) = await SeedPostingAsync(host);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/postings/{postingId}/transition")
        {
            Content = JsonContent.Create(new { toDisposition = "screened_out", reason = "Fictional reason", versionToken })
        };
        request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "test-corr-cmd-0001");
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.NotEmpty(capture.Lines);
        Assert.NotEmpty(RequestCompleted(capture, "test-corr-cmd-0001"));
        var commandStarting = Records(capture)
            .Where(r => r.TryGetProperty("eventId", out var e) && e.GetInt32() == 1001)
            .Where(r => r.GetProperty("msg").GetString()!.Contains("TransitionPosting", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(commandStarting);
        Assert.All(commandStarting, r => Assert.Equal("test-corr-cmd-0001", r.GetProperty("correlationId").GetString()));
    }

    [Fact]
    public async Task QueryRecords_WrittenDuringTheRequest_CarryItsCorrelationId_WhenDebugIsOn()
    {
        var (host, capture) = await StartAsync("rrtest_api_corr_query", debugApplication: true);
        await using var _ = host;

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/notes/{Guid.CreateVersion7()}");
        request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "test-corr-qry-0001");
        var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Assert.NotEmpty(capture.Lines);
        Assert.NotEmpty(RequestCompleted(capture, "test-corr-qry-0001"));
        var queryStarting = Records(capture)
            .Where(r => r.TryGetProperty("eventId", out var e) && e.GetInt32() == 2001)
            .ToList();
        Assert.NotEmpty(queryStarting);
        Assert.All(queryStarting, r => Assert.Equal("test-corr-qry-0001", r.GetProperty("correlationId").GetString()));
    }
}
