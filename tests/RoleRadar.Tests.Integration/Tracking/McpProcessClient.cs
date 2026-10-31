using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace RoleRadar.Tests.Integration.Tracking;

internal static class McpHostLocator
{
    public static string GetMcpDllPath()
    {
        var mcpDllPath = typeof(RoleRadar.Mcp.Stdio.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(mcpDllPath) || !File.Exists(mcpDllPath))
        {
            mcpDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Mcp.Stdio.dll");
        }

        Assert.True(File.Exists(mcpDllPath), $"Expected built MCP server assembly at '{mcpDllPath}'.");
        return mcpDllPath;
    }
}

internal sealed class McpProcessClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly System.Text.StringBuilder _stderr = new();
    private int _nextId = 1;

    private McpProcessClient(Process process)
    {
        _process = process;
    }

    public string GetStderr()
    {
        lock (_stderr)
        {
            return _stderr.ToString();
        }
    }

    public static async Task<McpProcessClient> StartAsync(string mcpDllPath, IReadOnlyDictionary<string, string> env, bool? forceContainer = null)
    {
        var runInContainer = forceContainer ?? (Environment.GetEnvironmentVariable("ROLERADAR_TEST_IMAGE") == "1");

        ProcessStartInfo psi;
        if (runInContainer)
        {
            var image = Environment.GetEnvironmentVariable("ROLERADAR_MCP_IMAGE")
                ?? Environment.GetEnvironmentVariable("ROLERADAR_IMAGE_TAG")
                ?? "roleradar-mcp:test";

            var runArgs = new System.Text.StringBuilder("run -i --rm --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --cap-drop ALL --security-opt no-new-privileges --add-host host.docker.internal:host-gateway ");
            foreach (var (k, v) in env)
            {
                var val = v;
                if (k.Equals("ROLERADAR_MONGO_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
                {
                    Hosting.ContainerTestHarness.ValidateNotDevStack(v);
                    val = val.Replace("127.0.0.1", "host.docker.internal").Replace("localhost", "host.docker.internal");
                }
                runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"-e {k}=\"{val}\" ");
            }
            runArgs.Append(image);

            psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = runArgs.ToString().Trim(),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{mcpDllPath}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var (k, v) in env)
            {
                psi.Environment[k] = v;
            }
        }

        var proc = Process.Start(psi)!;
        Assert.NotNull(proc);

        var client = new McpProcessClient(proc);
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (client._stderr)
                {
                    client._stderr.AppendLine(e.Data);
                }
            }
        };
        proc.BeginErrorReadLine();

        await client.InitializeAsync();
        return client;
    }

    private async Task InitializeAsync()
    {
        var initId = _nextId++;
        var initJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = initId,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "race-test", version = "1.0.0" }
            }
        });

        await _process.StandardInput.WriteLineAsync(initJson);
        await _process.StandardInput.FlushAsync();
        var initResponse = await _process.StandardOutput.ReadLineAsync();
        Assert.NotNull(initResponse);

        var notifJson = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        });
        await _process.StandardInput.WriteLineAsync(notifJson);
        await _process.StandardInput.FlushAsync();
    }

    public async Task<JsonElement> ListToolsAsync()
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = id,
            method = "tools/list"
        });

        await _process.StandardInput.WriteLineAsync(request);
        await _process.StandardInput.FlushAsync();

        var responseLine = await _process.StandardOutput.ReadLineAsync();
        Assert.NotNull(responseLine);

        using var doc = JsonDocument.Parse(responseLine);
        if (!doc.RootElement.TryGetProperty("result", out var resultElement))
        {
            throw new InvalidOperationException($"MCP error response: {responseLine}");
        }

        return resultElement.Clone();
    }

    public async Task<JsonDocument> CallToolRawAsync(string name, object arguments)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = id,
            method = "tools/call",
            @params = new
            {
                name = name,
                arguments = arguments
            }
        });

        await _process.StandardInput.WriteLineAsync(request);
        await _process.StandardInput.FlushAsync();

        var responseLine = await _process.StandardOutput.ReadLineAsync();
        Assert.NotNull(responseLine);

        return JsonDocument.Parse(responseLine);
    }

    public async Task<JsonElement> CallToolAsync(string name, object arguments)
    {
        var id = Interlocked.Increment(ref _nextId);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = id,
            method = "tools/call",
            @params = new
            {
                name = name,
                arguments = arguments
            }
        });

        await _process.StandardInput.WriteLineAsync(request);
        await _process.StandardInput.FlushAsync();

        var responseLine = await _process.StandardOutput.ReadLineAsync();
        Assert.NotNull(responseLine);

        using var doc = JsonDocument.Parse(responseLine);
        if (!doc.RootElement.TryGetProperty("result", out var resultElement))
        {
            throw new InvalidOperationException($"MCP error response: {responseLine}");
        }

        var content = resultElement.GetProperty("content");
        var text = content[0].GetProperty("text").GetString();
        Assert.NotNull(text);

        try
        {
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to parse tool text as JSON. Text was: '{text}'. Host stderr: {GetStderr()}", ex);
        }
    }

    /// <summary>
    /// Performs a read-only round trip so the host's JIT, Mongo driver connection pool and
    /// readiness probe are warm before a race barrier is released. Without this, N cold .NET
    /// processes starting concurrently on a small CI runner can exceed the product's 3 s
    /// readiness budget and report a false <c>DatastoreOffline</c>, which has nothing to do
    /// with the dedup logic the race tests verify. Retries only on <c>DatastoreOffline</c>.
    /// </summary>
    public async Task WarmUpAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        string lastResponse = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            var response = await CallToolAsync("note_search", new { query = "warmup", limit = 1 });
            if (response.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                return;
            }

            lastResponse = response.ToString();
            if (!lastResponse.Contains("DatastoreOffline", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Warm-up failed unexpectedly: {lastResponse}");
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"MCP host did not become ready within the warm-up window. Last response: {lastResponse}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _process.StandardInput.Close();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process already terminated
            }
        }
        finally
        {
            _process.Dispose();
        }
    }
}
