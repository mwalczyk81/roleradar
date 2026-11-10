using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

/// <summary>
/// Feature 014 US2 (FR-019 to FR-022, contracts/problem-details.md, ADR 0044): every error the API returns is an RFC 9457
/// problem document. For each of 400, 401, 403, 404, 409, 500 and 503 it checks the media type, the members, the headers and
/// that <c>traceId</c> is the bare W3C trace id of the request. The old envelope members (<c>ok</c>, <c>message</c>,
/// <c>details</c>, <c>correlationId</c>) are gone.
/// </summary>
public sealed class ProblemDetailsContractTests(SharedMongoFixture sharedMongo)
{
    private const string Base = "https://github.com/mwalczyk81/roleradar/blob/main/docs/api/problems.md#";
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string Traceparent = "00-" + TraceId + "-b7ad6b7169203331-01";
    private const string FaultHeader = "X-Test-Fault";
    private const string SecretText = "fictional-secret-detail-4c1d"; // gitleaks:allow (fictional redaction test vector, not a credential)

    private static readonly Regex HexTraceId = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    /// <summary>Throws from the authorization pipeline when a request carries <see cref="FaultHeader"/>: a forced unhandled fault.</summary>
    private sealed class FaultOnHeaderHandler : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            if (context.Resource is Microsoft.AspNetCore.Http.HttpContext http && http.Request.Headers.ContainsKey(FaultHeader))
            {
                throw new InvalidOperationException($"boom {SecretText} at db.fictional.invalid:27017");
            }

            return Task.CompletedTask;
        }
    }

    private static HttpRequestMessage Get(string url, Action<HttpRequestMessage>? tweak = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("traceparent", Traceparent);
        tweak?.Invoke(request);
        return request;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string errorCode)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status}, got {(int)response.StatusCode}. Body: {raw}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(raw);
        var body = document.RootElement.Clone();
        Assert.Equal((int)status, body.GetProperty("status").GetInt32());
        Assert.Equal(errorCode, body.GetProperty("errorCode").GetString());
        Assert.Equal(Base + errorCode, body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));

        var traceId = body.GetProperty("traceId").GetString();
        Assert.Matches(HexTraceId, traceId!);
        Assert.Equal(TraceId, traceId);

        foreach (var oldMember in new[] { "ok", "message", "details", "correlationId" })
        {
            Assert.False(body.TryGetProperty(oldMember, out _), $"The {(int)status} body still has '{oldMember}'.");
        }

        Assert.Equal(status == HttpStatusCode.ServiceUnavailable, response.Headers.Contains("Retry-After"));
        return body;
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
    public async Task Status400_Validation_IsAProblemDocument_WithErrors()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_problem_400");
        var unknown = Guid.CreateVersion7();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/postings/{unknown}/transition")
        {
            Content = JsonContent.Create(new { toDisposition = "not-a-disposition", versionToken = "x" })
        };
        request.Headers.TryAddWithoutValidation("traceparent", Traceparent);
        using var response = await host.Client.SendAsync(request);

        var body = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_error");
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Status400_FieldErrors_AreTheErrorsMember_KeyedByField()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_problem_400f");

        using var response = await host.Client.SendAsync(Get("/api/v1/insights/stats?window=nonsense"));

        var body = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_error");
        var errors = body.GetProperty("errors");
        Assert.True(errors.TryGetProperty("window", out var window) && window.GetArrayLength() >= 1, "Expected errors.window.");
        Assert.True(errors.TryGetProperty("asOf", out var asOf) && asOf.GetArrayLength() >= 1, "Expected errors.asOf.");
    }

    [Fact]
    public async Task Status400_ODataQueryError_IsAProblemDocument_NotTheODataErrorShape()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_problem_odata");

        using var response = await host.Client.SendAsync(Get("/odata/Postings?$orderby=content"));

        var body = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation_error");
        Assert.False(body.TryGetProperty("error", out _), "OData's native error shape must not be returned.");
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Status401_NoOrInvalidBearerToken_IsAProblemDocument(bool invalidToken)
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_problem_401");
        using var anonymous = new HttpClient { BaseAddress = host.Client.BaseAddress };

        using var request = Get($"/api/v1/postings/{Guid.CreateVersion7()}");
        if (invalidToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");
        }

        using var response = await anonymous.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "unauthorized");
        Assert.True(response.Headers.WwwAuthenticate.Count > 0, "The platform's WWW-Authenticate header must stay.");
    }

    [Fact]
    public async Task Status403_ANotAllowedSubject_IsAProblemDocument()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_problem_403");
        using var stranger = new HttpClient { BaseAddress = host.Client.BaseAddress };
        stranger.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.CreateValidOwnerToken(subject: "someone-else-fiction"));

        using var response = await stranger.SendAsync(Get($"/api/v1/postings/{Guid.CreateVersion7()}"));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "forbidden");
    }

    [Fact]
    public async Task Status404_IsAProblemDocument_WithTheMessageAsDetail()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_problem_404");

        using var response = await host.Client.SendAsync(Get($"/api/v1/postings/{Guid.CreateVersion7()}"));

        var body = await AssertProblemAsync(response, HttpStatusCode.NotFound, "not_found");
        Assert.Equal("Posting not found.", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Status409_AStaleVersionToken_IsAProblemDocument()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_problem_409");
        var (postingId, _) = await SeedPostingAsync(host);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/postings/{postingId}/transition")
        {
            Content = JsonContent.Create(new { toDisposition = "screened_out", reason = "Fictional reason", versionToken = Guid.CreateVersion7().ToString("D") })
        };
        request.Headers.TryAddWithoutValidation("traceparent", Traceparent);
        using var response = await host.Client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "concurrency_conflict");
    }

    [Fact]
    public async Task Status500_AForcedFault_IsAProblemDocument_WithNoExceptionText()
    {
        await using var host = await ApiContractTestHost.StartAsync(
            _sharedMongo,
            "rrtest_api_problem_500",
            builder => builder.Services.AddSingleton<IAuthorizationHandler, FaultOnHeaderHandler>());

        using var response = await host.Client.SendAsync(Get("/api/v1/postings/" + Guid.CreateVersion7(), r => r.Headers.Add(FaultHeader, "1")));
        var raw = await response.Content.ReadAsStringAsync();

        await AssertProblemAsync(response, HttpStatusCode.InternalServerError, "internal_error");
        foreach (var forbidden in new[] { SecretText, "fictional.invalid", "27017", "InvalidOperationException", " at RoleRadar", "StackTrace" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Status503_AnUnreachableStore_IsAProblemDocument_WithRetryAfter_AndNoConnectionDetails()
    {
        await using var host = await ApiContractTestHost.StartAsync(
            _sharedMongo,
            "rrtest_api_problem_503",
            builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MongoConnection"] = "mongodb://127.0.0.1:1/?directConnection=true"
            }));
        host.Client.Timeout = TimeSpan.FromSeconds(30);

        using var response = await host.Client.SendAsync(Get("/odata/Postings"));
        var raw = await response.Content.ReadAsStringAsync();

        var body = await AssertProblemAsync(response, HttpStatusCode.ServiceUnavailable, "service_unavailable");
        Assert.Equal("5", Assert.Single(response.Headers.GetValues("Retry-After")));
        Assert.Equal("The data store is not reachable. Try again shortly.", body.GetProperty("detail").GetString());
        foreach (var forbidden in new[] { "127.0.0.1", "mongodb://", "MongoDB.", "start-services", "docker compose" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }
}
