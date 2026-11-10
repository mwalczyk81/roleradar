using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using RoleRadar.Api.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Api;

/// <summary>
/// Feature 013 US3 (FR-024): an exception that escapes a request becomes a 503 when the data store did not answer and a
/// 500 otherwise, both with the standard envelope and the correlation id, and never with the exception's text, stack,
/// host or a remediation command (those go to the scrubbed log only).
/// </summary>
public sealed class ApiExceptionHandlerTests
{
    private const string CorrelationValue = "test-corr-0500";
    private const string SecretText = "fictional-secret-detail-9f3a"; // gitleaks:allow (fictional redaction test vector, not a credential)

    private sealed class ListLogger : ILogger<ApiExceptionHandler>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }

    private static async Task<(bool Handled, int Status, JsonElement Body, string Raw, IHeaderDictionary Headers, ListLogger Logger)> RunAsync(
        Exception exception,
        bool requestAborted = false)
    {
        using var aborted = new CancellationTokenSource();
        if (requestAborted)
        {
            await aborted.CancelAsync();
        }

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            RequestAborted = aborted.Token
        };
        context.Items[CorrelationMiddleware.ItemKey] = CorrelationValue;
        context.Response.Body = new MemoryStream();
        _ = new HttpContextAccessor { HttpContext = context };

        var logger = new ListLogger();
        var handled = await new ApiExceptionHandler(logger).TryHandleAsync(context, exception, CancellationToken.None);

        context.Response.Body.Position = 0;
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        return (handled, context.Response.StatusCode, document.RootElement.Clone(), raw, context.Response.Headers, logger);
    }

    private static MongoConnectionException Unavailable() =>
        new(new ConnectionId(new ServerId(new ClusterId(1), new DnsEndPoint("db.fictional.invalid", 27017))), $"connect failed {SecretText}");

    [Fact]
    public async Task AnUnavailableDatastore_Is503_ServiceUnavailable_WithRetryAfter_AndTheCorrelationId()
    {
        var (handled, status, body, _, headers, _) = await RunAsync(Unavailable());

        Assert.True(handled);
        Assert.Equal(503, status);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("service_unavailable", body.GetProperty("errorCode").GetString());
        Assert.Equal(CorrelationValue, body.GetProperty("correlationId").GetString());
        Assert.Equal("5", headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task AnyOtherException_Is500_InternalError_WithTheCorrelationId_AndNoRetryAfter()
    {
        var (handled, status, body, _, headers, _) = await RunAsync(new InvalidOperationException($"boom {SecretText}"));

        Assert.True(handled);
        Assert.Equal(500, status);
        Assert.False(body.GetProperty("ok").GetBoolean());
        Assert.Equal("internal_error", body.GetProperty("errorCode").GetString());
        Assert.Equal(CorrelationValue, body.GetProperty("correlationId").GetString());
        Assert.False(headers.ContainsKey("Retry-After"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NeitherBody_ContainsTheExceptionMessage_Stack_Host_OrARemediationCommand(bool unavailable)
    {
        Exception exception = unavailable ? Unavailable() : new InvalidOperationException($"boom {SecretText} at db.fictional.invalid");

        var (_, _, body, raw, _, _) = await RunAsync(exception);

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        foreach (var forbidden in new[] { SecretText, "fictional.invalid", "27017", "StackTrace", " at RoleRadar", "start-services", "docker compose", "pwsh", "System.", "MongoDB." })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheExceptionIsLogged_AtErrorLevel_WithTheExceptionAttached(bool unavailable)
    {
        Exception exception = unavailable ? Unavailable() : new InvalidOperationException("boom");

        var (_, _, _, _, _, logger) = await RunAsync(exception);

        var entry = Assert.Single(logger.Entries, e => e.Level >= LogLevel.Warning);
        Assert.Same(exception, entry.Exception);
    }

    [Fact]
    public async Task AClientDisconnect_IsAbsorbedQuietly_WithoutAnErrorRecord()
    {
        var (handled, _, _, _, _, logger) = await RunAsync(new OperationCanceledException("the caller gave up"), requestAborted: true);

        Assert.True(handled);
        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task ACancellation_WhileTheClientIsStillConnected_IsA500_NotA503()
    {
        var (handled, status, body, _, _, _) = await RunAsync(new OperationCanceledException("an internal timeout"));

        Assert.True(handled);
        Assert.Equal(500, status);
        Assert.Equal("internal_error", body.GetProperty("errorCode").GetString());
    }
}
