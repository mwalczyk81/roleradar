using System.Diagnostics;
using System.Text;

namespace RoleRadar.Tests.Integration.Hosting;

public sealed class ContainerExecutionResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public TimeSpan TimeToFirstResponse { get; init; }
}

public static class ContainerTestHarness
{
    public const string DefaultCliImage = "roleradar-cli:test";
    public const string DefaultMcpImage = "roleradar-mcp:test";

    private static readonly SemaphoreSlim BuildLock = new(1, 1);
    private static bool _imagesBuilt;

    public static string ResolveCliImage() =>
        Environment.GetEnvironmentVariable("ROLERADAR_CLI_IMAGE")
        ?? Environment.GetEnvironmentVariable("ROLERADAR_IMAGE_TAG")
        ?? DefaultCliImage;

    public static string ResolveMcpImage() =>
        Environment.GetEnvironmentVariable("ROLERADAR_MCP_IMAGE")
        ?? Environment.GetEnvironmentVariable("ROLERADAR_IMAGE_TAG")
        ?? DefaultMcpImage;

    public static async Task EnsureImagesBuiltAsync()
    {
        if (_imagesBuilt)
        {
            return;
        }

        await BuildLock.WaitAsync();
        try
        {
            if (_imagesBuilt)
            {
                return;
            }

            var repoRoot = FindRepoRoot();

            // Check if images already exist (e.g. CI pre-built)
            var cliImage = ResolveCliImage();
            var mcpImage = ResolveMcpImage();

            if (!await ImageExistsAsync(cliImage))
            {
                await RunProcessAsync("docker", $"build --target cli -t {cliImage} .", repoRoot, 300);
            }

            if (!await ImageExistsAsync(mcpImage))
            {
                await RunProcessAsync("docker", $"build --target mcp -t {mcpImage} .", repoRoot, 300);
            }

            _imagesBuilt = true;
        }
        finally
        {
            BuildLock.Release();
        }
    }

    private static async Task<bool> ImageExistsAsync(string imageTag)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"image inspect {imageTag}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return false;
        }

        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    public static async Task<ContainerExecutionResult> RunCliAsync(
        string[] arguments,
        IDictionary<string, string>? environment = null,
        string? stdinInput = null,
        int timeoutSeconds = 30)
    {
        await EnsureImagesBuiltAsync();
        return await RunRawAsync(ResolveCliImage(), arguments, environment, stdinInput, timeoutSeconds);
    }

    public static async Task<ContainerExecutionResult> RunMcpAsync(
        string? inputJsonRpc = null,
        IDictionary<string, string>? environment = null,
        int timeoutSeconds = 30)
    {
        await EnsureImagesBuiltAsync();
        return await RunRawAsync(ResolveMcpImage(), [], environment, inputJsonRpc, timeoutSeconds);
    }

    public static async Task<ContainerExecutionResult> RunRawAsync(
        string imageTag,
        string[] arguments,
        IDictionary<string, string>? environment = null,
        string? stdinInput = null,
        int timeoutSeconds = 30,
        string? entrypoint = null)
    {
        var runArgs = new StringBuilder("run --rm -i ");
        runArgs.Append("--read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --cap-drop ALL --security-opt no-new-privileges ");

        if (!string.IsNullOrEmpty(entrypoint))
        {
            runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"--entrypoint {entrypoint} ");
        }

        if (environment is not null)
        {
            foreach (var (k, v) in environment)
            {
                // Escape quotes if needed
                runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"-e {k}=\"{v}\" ");
            }
        }

        runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"{imageTag} ");
        foreach (var arg in arguments)
        {
            runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"\"{arg}\" ");
        }

        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = runArgs.ToString().Trim(),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var stopwatch = Stopwatch.StartNew();
        using var process = new Process { StartInfo = psi };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdoutBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timeToFirstResponse = TimeSpan.Zero;
        if (!string.IsNullOrEmpty(stdinInput))
        {
            var responseReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnDataReceived(object _, DataReceivedEventArgs args)
            {
                if (args.Data?.Contains("jsonrpc", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (timeToFirstResponse == TimeSpan.Zero)
                    {
                        timeToFirstResponse = stopwatch.Elapsed;
                    }
                    responseReceived.TrySetResult(true);
                }
            }

            process.OutputDataReceived += OnDataReceived;

            await process.StandardInput.WriteAsync(stdinInput);
            await process.StandardInput.FlushAsync();

            // Wait for response frame or timeout before closing stdin
            await Task.WhenAny(responseReceived.Task, Task.Delay(5000));
            process.OutputDataReceived -= OnDataReceived;
        }

        process.StandardInput.Close();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may have already exited
            }
            throw new TimeoutException($"Container execution timed out after {timeoutSeconds} seconds.");
        }

        stopwatch.Stop();

        return new ContainerExecutionResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdoutBuilder.ToString(),
            StandardError = stderrBuilder.ToString(),
            Duration = stopwatch.Elapsed,
            TimeToFirstResponse = timeToFirstResponse == TimeSpan.Zero ? stopwatch.Elapsed : timeToFirstResponse
        };
    }

    private static async Task RunProcessAsync(string fileName, string arguments, string workingDirectory, int timeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process '{fileName} {arguments}' timed out after {timeoutSeconds} seconds.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{fileName} {arguments}' failed with exit code {process.ExitCode}.\nStdout:\n{stdout}\nStderr:\n{stderr}");
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

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new InvalidOperationException("Could not find repository root containing RoleRadar.sln");
    }
}
