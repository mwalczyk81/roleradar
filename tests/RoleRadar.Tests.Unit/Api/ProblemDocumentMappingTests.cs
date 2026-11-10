using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Api.Problems;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Api;

/// <summary>
/// Feature 014 US2 (FR-019 to FR-022, contracts/problem-details.md, ADR 0044): a Result error maps to the RFC 9457 problem
/// document the contract names. It replaces the 013 envelope mapping tests: the statuses are the same, the body is
/// <c>application/problem+json</c> with <c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c>, <c>errorCode</c> and
/// <c>traceId</c>, field errors are <c>errors</c>, and an unreachable data store is still a 503 with <c>Retry-After</c> and a
/// fixed text that carries no endpoint, exception text or remediation command.
/// </summary>
public sealed class ProblemDocumentMappingTests
{
    private const string Base = "https://github.com/mwalczyk81/roleradar/blob/main/docs/api/problems.md#";

    private sealed record Run(int Status, string? ContentType, JsonElement Body, string Raw, IHeaderDictionary Headers, string TraceId);

    private static async Task<Run> RunAsync(Func<IResult> map)
    {
        using var activity = new Activity("problem-mapping-test").Start();
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();

        await map().ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(raw);
        return new Run(
            context.Response.StatusCode,
            context.Response.ContentType,
            document.RootElement.Clone(),
            raw,
            context.Response.Headers,
            activity.TraceId.ToHexString());
    }

    private static ResultError Offline() => new(
        ErrorCode.DatastoreOffline,
        MongoReadinessProbe.FormatOfflineMessage("mongodb://svc-user:s3cret-fictional@db.fictional.invalid:27017/?replicaSet=rs0"),
        new Dictionary<string, object?>
        {
            ["endpoint"] = "db.fictional.invalid:27017 (rs0)",
            ["remediationCommand"] = MongoReadinessProbe.StartServicesCommand
        });

    private static void AssertProblem(Run run, int status, string errorCode)
    {
        Assert.Equal(status, run.Status);
        Assert.StartsWith("application/problem+json", run.ContentType, StringComparison.Ordinal);
        Assert.Equal(status, run.Body.GetProperty("status").GetInt32());
        Assert.Equal(errorCode, run.Body.GetProperty("errorCode").GetString());
        Assert.Equal(Base + errorCode, run.Body.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(run.Body.GetProperty("title").GetString()));
        Assert.Equal(run.TraceId, run.Body.GetProperty("traceId").GetString());
        foreach (var oldMember in new[] { "ok", "message", "details", "correlationId", "instance" })
        {
            Assert.False(run.Body.TryGetProperty(oldMember, out _), $"The problem document still has '{oldMember}'.");
        }
    }

    [Fact]
    public void TheTypePrefix_IsTheDocumentationUrl_InOneConstant()
    {
        Assert.Equal(Base, ProblemTypes.TypeUriPrefix);
    }

    [Fact]
    public async Task Validation_Is400_ValidationError_WithTheMessageAsDetail_AndNoRetryAfter()
    {
        var run = await RunAsync(() => ApiProblems.FromResultError(new ResultError(ErrorCode.ValidationError, "Fictional field is required.")));

        AssertProblem(run, 400, "validation_error");
        Assert.Equal("Fictional field is required.", run.Body.GetProperty("detail").GetString());
        Assert.False(run.Body.TryGetProperty("errors", out _));
        Assert.False(run.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task Validation_WithAField_CarriesErrors_KeyedByTheFieldName()
    {
        var run = await RunAsync(() => ApiProblems.FromResultError(new ResultError(
            ErrorCode.ValidationError,
            "too long",
            new Dictionary<string, object?> { ["field"] = "title" })));

        AssertProblem(run, 400, "validation_error");
        var errors = run.Body.GetProperty("errors");
        Assert.Equal(["too long"], errors.GetProperty("title").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public async Task Validation_WithANestedField_KeepsTheFieldNameVerbatim()
    {
        var run = await RunAsync(() => ApiProblems.FromResultError(new ResultError(
            ErrorCode.ValidationError,
            "not an address",
            new Dictionary<string, object?> { ["field"] = "contact.email" })));

        var errors = run.Body.GetProperty("errors");
        Assert.Equal(["not an address"], errors.GetProperty("contact.email").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public async Task ValidationWithSeveralFields_GroupsTheIssuesPerField()
    {
        var run = await RunAsync(() => ApiProblems.Validation(
            "The window or as-of date is missing or invalid.",
            new Dictionary<string, string[]>
            {
                ["window"] = ["window is required."],
                ["asOf"] = ["asOf is required.", "asOf must be yyyy-MM-dd."]
            }));

        AssertProblem(run, 400, "validation_error");
        var errors = run.Body.GetProperty("errors");
        Assert.Single(errors.GetProperty("window").EnumerateArray());
        Assert.Equal(2, errors.GetProperty("asOf").GetArrayLength());
    }

    [Fact]
    public async Task ANullError_Is400_WithTheGenericValidationText()
    {
        var run = await RunAsync(() => ApiProblems.FromResultError(null));

        AssertProblem(run, 400, "validation_error");
        Assert.Equal("The request payload failed validation.", run.Body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task NotFound_Is404_NotFound_WithTheMessageAsDetail()
    {
        var run = await RunAsync(() => ApiProblems.FromResultError(new ResultError(ErrorCode.NotFound, "Fictional posting not found.")));

        AssertProblem(run, 404, "not_found");
        Assert.Equal("Fictional posting not found.", run.Body.GetProperty("detail").GetString());
        Assert.False(run.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task StaleVersionToken_Is409_ConcurrencyConflict()
    {
        var run = await RunAsync(() => ApiProblems.FromResultError(new ResultError(ErrorCode.StaleVersionToken, "Fictional version is stale.")));

        AssertProblem(run, 409, "concurrency_conflict");
        Assert.Equal("Fictional version is stale.", run.Body.GetProperty("detail").GetString());
        Assert.False(run.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task DatastoreOffline_Is503_ServiceUnavailable_WithRetryAfterFive_AndTheFixedText()
    {
        var run = await RunAsync(() => ApiProblems.FromResultError(Offline()));

        AssertProblem(run, 503, "service_unavailable");
        Assert.Equal("The data store is not reachable. Try again shortly.", run.Body.GetProperty("detail").GetString());
        Assert.Equal("5", run.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task DatastoreOffline_Body_CarriesNoEndpoint_Credential_OrRemediationCommand()
    {
        var run = await RunAsync(() => ApiProblems.FromResultError(Offline()));

        foreach (var forbidden in new[] { "fictional.invalid", "27017", "s3cret", "svc-user", "start-services", "docker compose", "pwsh", "mongodb://" })
        {
            Assert.DoesNotContain(forbidden, run.Raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task InternalError_Is500_WithTheFixedText_AndNoRetryAfter()
    {
        var run = await RunAsync(ApiProblems.InternalError);

        AssertProblem(run, 500, "internal_error");
        Assert.Equal("The request failed unexpectedly. Try again, and check the logs if it keeps happening.", run.Body.GetProperty("detail").GetString());
        Assert.False(run.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task OnlyA503_CarriesRetryAfter()
    {
        foreach (var code in new[] { ErrorCode.ValidationError, ErrorCode.NotFound, ErrorCode.StaleVersionToken, ErrorCode.InvalidTransition })
        {
            var run = await RunAsync(() => ApiProblems.FromResultError(new ResultError(code, "Fictional message.")));

            Assert.False(run.Headers.ContainsKey("Retry-After"), $"{code} carries Retry-After.");
        }
    }

    [Fact]
    public void EveryCode_HasAnAnchorInTheDocumentationPageTheTypePointsAt()
    {
        var page = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "api", "problems.md"));

        foreach (var code in ProblemTypes.Codes)
        {
            Assert.Contains($"{Environment.NewLine}### {code}{Environment.NewLine}".Replace("\r\n", "\n", StringComparison.Ordinal), page.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "RoleRadar.sln")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
