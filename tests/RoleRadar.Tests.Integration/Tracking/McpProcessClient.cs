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

    public static async Task<McpProcessClient> StartAsync(string mcpDllPath, IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo
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

    public async ValueTask DisposeAsync()
    {
        try
        {
            _process.StandardInput.Close();
            await _process.WaitForExitAsync();
        }
        finally
        {
            _process.Dispose();
        }
    }
}
