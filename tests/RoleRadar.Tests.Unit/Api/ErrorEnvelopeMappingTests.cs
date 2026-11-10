using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Api.Envelopes;
using RoleRadar.Api.Observability;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Api;

/// <summary>
/// Feature 013 US3 (FR-024, contracts/http-correlation-and-errors.md): a Result error maps to the status and envelope the
/// contract names. 400, 404 and 409 are unchanged; an unreachable data store is 503 service_unavailable with Retry-After
/// and a fixed message that carries no endpoint, exception text or remediation command.
/// </summary>
public sealed class ErrorEnvelopeMappingTests
{
    private const string CorrelationValue = "test-corr-0503";

    private static async Task<(int Status, JsonElement Body, string RawBody, IHeaderDictionary Headers)> RunAsync(Func<IResult> map)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Items[CorrelationMiddleware.ItemKey] = CorrelationValue;
        context.Response.Body = new MemoryStream();

        // The envelope reads the current request through the framework accessor, whose current context is ambient.
        _ = new HttpContextAccessor { HttpContext = context };

        var result = map();
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(raw);
        return (context.Response.StatusCode, document.RootElement.Clone(), raw, context.Response.Headers);
    }

    private static ResultError Offline() => new(
        ErrorCode.DatastoreOffline,
        MongoReadinessProbe.FormatOfflineMessage("mongodb://svc-user:s3cret-fictional@db.fictional.invalid:27017/?replicaSet=rs0"),
        new Dictionary<string, object?>
        {
            ["endpoint"] = "db.fictional.invalid:27017 (rs0)",
            ["remediationCommand"] = MongoReadinessProbe.StartServicesCommand
        });

    [Fact]
    public async Task DatastoreOffline_Is503_ServiceUnavailable_WithRetryAfterFive_AndTheCorrelationId()
    {
        var (status, body, _, headers) = await RunAsync(() => ErrorEnvelope.FromResultError(Offline()));

        Assert.Equal(503, status);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("service_unavailable", body.GetProperty("errorCode").GetString());
        Assert.Equal(CorrelationValue, body.GetProperty("correlationId").GetString());
        Assert.Equal("5", headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task DatastoreOffline_Body_CarriesNoEndpoint_Credential_OrRemediationCommand()
    {
        var (_, body, raw, _) = await RunAsync(() => ErrorEnvelope.FromResultError(Offline()));

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        foreach (var forbidden in new[] { "fictional.invalid", "27017", "s3cret", "svc-user", "start-services", "docker compose", "pwsh", "mongodb://" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Validation_StaysA400()
    {
        var (status, body, _, headers) = await RunAsync(() => ErrorEnvelope.FromResultError(new ResultError(ErrorCode.ValidationError, "Fictional field is required.")));

        Assert.Equal(400, status);
        Assert.Equal("validation_error", body.GetProperty("errorCode").GetString());
        Assert.Equal("Fictional field is required.", body.GetProperty("message").GetString());
        Assert.False(headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task NotFound_StaysA404()
    {
        var (status, body, _, headers) = await RunAsync(() => ErrorEnvelope.FromResultError(new ResultError(ErrorCode.NotFound, "Fictional posting not found.")));

        Assert.Equal(404, status);
        Assert.Equal("not_found", body.GetProperty("errorCode").GetString());
        Assert.False(headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task StaleVersionToken_StaysA409()
    {
        var (status, body, _, headers) = await RunAsync(() => ErrorEnvelope.FromResultError(new ResultError(ErrorCode.StaleVersionToken, "Fictional version is stale.")));

        Assert.Equal(409, status);
        Assert.Equal("concurrency_conflict", body.GetProperty("errorCode").GetString());
        Assert.False(headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task EveryEnvelope_CarriesTheCorrelationId()
    {
        foreach (var code in new[] { ErrorCode.ValidationError, ErrorCode.NotFound, ErrorCode.StaleVersionToken, ErrorCode.DatastoreOffline })
        {
            var (_, body, _, _) = await RunAsync(() => ErrorEnvelope.FromResultError(new ResultError(code, "Fictional message.")));

            Assert.Equal(CorrelationValue, body.GetProperty("correlationId").GetString());
        }
    }
}
