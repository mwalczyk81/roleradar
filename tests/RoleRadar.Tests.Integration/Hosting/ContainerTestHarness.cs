using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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

    public static void ValidateNotDevStack(string connectionString)
    {
        if (Environment.GetEnvironmentVariable("ROLERADAR_ALLOW_DEV_STACK") == "1")
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var mongoUrl = new MongoDB.Driver.MongoUrl(connectionString);
        foreach (var server in mongoUrl.Servers)
        {
            var isPort27017 = server.Port == 27017 || !connectionString.Contains(':', StringComparison.Ordinal);
            if (isPort27017)
            {
                var h = server.Host.Trim('[', ']');
                if (h.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Targeting the local developer MongoDB stack ({server.Host}:{server.Port}) is strictly forbidden in tests to prevent data loss. Use isolated Testcontainers or set ROLERADAR_ALLOW_DEV_STACK=1 to override.");
                }
            }
        }
    }

    public static void ValidateDatabaseNameForDrop(string databaseName)
    {
        if (string.Equals(databaseName, "roleradar", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to drop protected default database 'roleradar' in test cleanup.");
        }
    }

    public static string? HostUserAndGroup { get; } = ResolveHostUserAndGroup();

    private static string? ResolveHostUserAndGroup()
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "id",
                Arguments = "-u",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var uProc = Process.Start(psi);
            var uid = uProc?.StandardOutput.ReadToEnd().Trim();
            uProc?.WaitForExit();

            psi.Arguments = "-g";
            using var gProc = Process.Start(psi);
            var gid = gProc?.StandardOutput.ReadToEnd().Trim();
            gProc?.WaitForExit();

            if (!string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(gid))
            {
                return $"{uid}:{gid}";
            }
        }
        catch
        {
            // Ignore error resolving id on non-POSIX or minimal containers
        }

        return null;
    }

    public static void EnsureWritableDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        Directory.CreateDirectory(directoryPath);
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(directoryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            }
            catch
            {
                try
                {
                    Process.Start("chmod", $"777 \"{directoryPath}\"")?.WaitForExit();
                }
                catch { }
            }
        }
    }

    public static string CreateSharedTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        EnsureWritableDirectory(path);
        return path;
    }

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
        int timeoutSeconds = 30,
        IEnumerable<string>? volumes = null,
        string? network = null)
    {
        await EnsureImagesBuiltAsync();
        return await RunRawAsync(ResolveCliImage(), arguments, environment, stdinInput, timeoutSeconds, volumes: volumes, network: network);
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
        string? entrypoint = null,
        IEnumerable<string>? volumes = null,
        string? network = null)
    {
        var runArgs = new StringBuilder("run --rm -i ");
        runArgs.Append("--read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --cap-drop ALL --security-opt no-new-privileges ");

        if (!string.IsNullOrWhiteSpace(network))
        {
            runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"--network {network} ");
        }

        if (!string.IsNullOrEmpty(entrypoint))
        {
            runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"--entrypoint {entrypoint} ");
        }

        var hasWritableMount = false;
        if (volumes is not null)
        {
            foreach (var vol in volumes)
            {
                var match = Regex.Match(vol, @"^(.+?):(/[^:]+)(?::([a-z,]+))?$");
                if (match.Success)
                {
                    var mode = match.Groups[3].Value;
                    if (string.IsNullOrEmpty(mode) || mode.Contains("rw", StringComparison.OrdinalIgnoreCase))
                    {
                        hasWritableMount = true;
                    }
                    EnsureWritableDirectory(match.Groups[1].Value);
                }
                else
                {
                    hasWritableMount = true;
                }
                runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"-v \"{vol}\" ");
            }
        }

        if (hasWritableMount && !string.IsNullOrEmpty(HostUserAndGroup))
        {
            runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"--user \"{HostUserAndGroup}\" ");
        }

        if (environment is not null)
        {
            foreach (var (k, v) in environment)
            {
                if (k.Equals("ROLERADAR_MONGO_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateNotDevStack(v);
                }

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

        var timeToFirstResponse = TimeSpan.Zero;
        var responseReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdoutBuilder.AppendLine(e.Data);
                if (e.Data.Contains("jsonrpc", StringComparison.OrdinalIgnoreCase))
                {
                    if (timeToFirstResponse == TimeSpan.Zero)
                    {
                        timeToFirstResponse = stopwatch.Elapsed;
                    }
                    responseReceived.TrySetResult(true);
                }
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

        if (!string.IsNullOrEmpty(stdinInput))
        {
            await process.StandardInput.WriteAsync(stdinInput);
            await process.StandardInput.FlushAsync();

            // Wait for response frame or timeout before closing stdin
            await Task.WhenAny(responseReceived.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
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

    public static string FindRepoRoot()
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
