using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace RoleRadar.Tests.Integration.Api;

/// <summary>
/// Feature 013 US3 (FR-024, FR-025, SC-006): while its data store is stalled the API answers a read and a write with a 503
/// <c>service_unavailable</c> envelope inside 5 s, and it recovers by itself, with the same host, within 30 s of the store
/// coming back.
///
/// The store is a dedicated Testcontainers replica set on a host port the Docker daemon assigns (never 27017, 5001 to
/// 5004 or 5101 to 5103, which the dev and demo stacks publish), and the API host is started in process against it. The
/// outage is <c>docker pause</c> of that one container. Nothing here reaches a developer's Mongo or stack.
/// </summary>
[Trait("Category", "Slow")]
[Collection("DatastoreOutageCollection")]
public sealed class DatastoreOutageIntegrationTests : IAsyncLifetime
{
    private const string Owner = "0192e4a1-0000-7000-8000-0000000000a1";
    private static readonly TimeSpan ResponseBound = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RecoveryBound = TimeSpan.FromSeconds(30);
    private static readonly int[] ReservedPorts = [27017, 5001, 5002, 5003, 5004, 5101, 5102, 5103];

    private ODataTestHost _host = null!;
    private bool _paused;

    public async ValueTask InitializeAsync()
    {
        _host = new ODataTestHost($"rrtest_outage_{Guid.CreateVersion7():N}", Owner);
        await _host.StartDatabaseAsync();

        Assert.DoesNotContain(_host.ContainerPort, ReservedPorts);

        await _host.StartApiAsync();

        // Longer than every bound under test, so a hang shows as a failed assertion, not as a client timeout of ours.
        _host.Client.Timeout = TimeSpan.FromSeconds(45);
    }

    public async ValueTask DisposeAsync()
    {
        if (_paused)
        {
            try
            {
                await _host.UnpauseDatabaseAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The container is disposed next either way.
            }
        }

        await _host.DisposeAsync();
    }

    private static void Note(string text) => TestContext.Current.TestOutputHelper?.WriteLine(text);

    private async Task<HttpResponseMessage> ReadPostingsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.Token(Owner));
        return await _host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> TransitionPostingAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/postings/{Guid.CreateVersion7()}/transition")
        {
            Content = JsonContent.Create(new
            {
                toDisposition = "screened_out",
                reason = "Fictional outage test reason",
                versionToken = Guid.CreateVersion7().ToString("D")
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.Token(Owner));
        return await _host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertServiceUnavailableEnvelopeAsync(HttpResponseMessage response, string what)
    {
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.StatusCode == HttpStatusCode.ServiceUnavailable, $"{what}: expected 503, got {(int)response.StatusCode}. Body: {raw}");

        using var document = JsonDocument.Parse(raw);
        var body = document.RootElement;
        Assert.Equal(503, body.GetProperty("status").GetInt32());
        Assert.Equal("service_unavailable", body.GetProperty("errorCode").GetString());
        Assert.Matches("^[0-9a-f]{32}$", body.GetProperty("traceId").GetString()!);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        foreach (var forbidden in new[] { "27017", "mongodb://", "start-services", "docker compose", "MongoDB.", " at RoleRadar" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task AStalledStore_Gives503Inside5Seconds_ForAReadAndAWrite_AndTheApiRecoversWithoutARestart()
    {
        // Baseline on the live store, so the 503s below are caused by the pause and the paths are proven reachable:
        // a read is 200, and a write for an unknown posting gets as far as the handler and is a 404.
        using (var healthyRead = await ReadPostingsAsync())
        {
            Assert.Equal(HttpStatusCode.OK, healthyRead.StatusCode);
        }

        using (var healthyWrite = await TransitionPostingAsync())
        {
            Assert.Equal(HttpStatusCode.NotFound, healthyWrite.StatusCode);
        }

        await _host.PauseDatabaseAsync();
        _paused = true;

        var readClock = Stopwatch.StartNew();
        using var stalledRead = await ReadPostingsAsync();
        readClock.Stop();
        Note($"MEASURED read 503: {readClock.Elapsed.TotalMilliseconds:0} ms (bound {ResponseBound.TotalMilliseconds:0} ms)");

        var writeClock = Stopwatch.StartNew();
        using var stalledWrite = await TransitionPostingAsync();
        writeClock.Stop();
        Note($"MEASURED write 503: {writeClock.Elapsed.TotalMilliseconds:0} ms (bound {ResponseBound.TotalMilliseconds:0} ms)");

        await AssertServiceUnavailableEnvelopeAsync(stalledRead, "read while paused");
        await AssertServiceUnavailableEnvelopeAsync(stalledWrite, "write while paused");
        Assert.True(readClock.Elapsed < ResponseBound, $"The read took {readClock.Elapsed.TotalMilliseconds:0} ms; the bound is {ResponseBound.TotalMilliseconds:0} ms.");
        Assert.True(writeClock.Elapsed < ResponseBound, $"The write took {writeClock.Elapsed.TotalMilliseconds:0} ms; the bound is {ResponseBound.TotalMilliseconds:0} ms.");

        await _host.UnpauseDatabaseAsync();
        _paused = false;

        // The same host and the same Mongo client: no restart, no recreate.
        var recoveryClock = Stopwatch.StartNew();
        var lastStatus = HttpStatusCode.ServiceUnavailable;
        while (recoveryClock.Elapsed < RecoveryBound)
        {
            using var attempt = await ReadPostingsAsync();
            lastStatus = attempt.StatusCode;
            if (lastStatus == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        recoveryClock.Stop();
        Note($"MEASURED recovery to 200: {recoveryClock.Elapsed.TotalMilliseconds:0} ms (bound {RecoveryBound.TotalMilliseconds:0} ms)");

        Assert.True(
            lastStatus == HttpStatusCode.OK && recoveryClock.Elapsed < RecoveryBound,
            $"No 200 within {RecoveryBound.TotalSeconds:0} s of unpausing; the last status was {(int)lastStatus}.");
    }
}
