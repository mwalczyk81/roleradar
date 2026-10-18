using System.Diagnostics;
using Xunit;

namespace RoleRadar.Tests.Integration.Cli;

public sealed class InvalidConnectionStringSecurityIntegrationTests
{
    private const string ExpectedFixedErrorMessage = "ROLERADAR_MONGO_CONNECTION_STRING is not a valid MongoDB connection string. The value is hidden.";
    private const string TestSecretPassword = "SuperSecretInvalidPassword123!";

    public static TheoryData<string> InvalidConnectionStringsWithSecret =>
        [
            // 1. scheme-less user:pass@host:port
            $"appuser:{TestSecretPassword}@localhost:27017",

            // 2. mongodb://user:pass@ (no host)
            $"mongodb://appuser:{TestSecretPassword}@",

            // 3. bad port
            $"mongodb://appuser:{TestSecretPassword}@localhost:999999",

            // 4. unbalanced @
            $"mongodb://appuser:{TestSecretPassword}@@localhost:27017",

            // 5. srv with an unresolvable host (must have at least 3 parts for srv, e.g. host.invalid.internal)
            $"mongodb+srv://appuser:{TestSecretPassword}@nonexistent.cluster.invalid"
        ];

    private static string GetCliDllPath()
    {
        var cliDllPath = typeof(RoleRadar.Cli.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(cliDllPath) || !File.Exists(cliDllPath))
        {
            cliDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        }

        Assert.True(File.Exists(cliDllPath), $"Expected built CLI assembly at '{cliDllPath}'.");
        return cliDllPath;
    }

    private static string GetMcpDllPath()
    {
        var mcpDllPath = typeof(Mcp.Stdio.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(mcpDllPath) || !File.Exists(mcpDllPath))
        {
            mcpDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Mcp.Stdio.dll");
        }

        Assert.True(File.Exists(mcpDllPath), $"Expected built MCP assembly at '{mcpDllPath}'.");
        return mcpDllPath;
    }

    [Theory]
    [MemberData(nameof(InvalidConnectionStringsWithSecret))]
    public async Task CliCommands_WithInvalidConnectionString_NeverEchoSecret_AndPrintFixedErrorWithoutStackTrace(string invalidConnStr)
    {
        var cliDll = GetCliDllPath();
        var tempFile = Path.GetTempFileName();

        try
        {
            await File.WriteAllTextAsync(tempFile, "# Test Heading\nTest content.");

            var commandsToTest = new[]
            {
                "status",
                "status --json",
                "wipe --confirm WIPE-ALL-ROLERADAR-DATA",
                $"import \"{tempFile}\" --dry-run",
                "backup",
                "restore non-existent.rrbak"
            };

            var env = new Dictionary<string, string>
            {
                ["ROLERADAR_MONGO_CONNECTION_STRING"] = invalidConnStr,
                ["ROLERADAR_MONGO_DATABASE"] = "roleradar_test_db"
            };

            foreach (var cmd in commandsToTest)
            {
                var (exitCode, stdout, stderr) = await RunProcessAsync(cliDll, cmd, env);

                // Exit code must be non-zero
                Assert.NotEqual(0, exitCode);

                var combinedOutput = stdout + "\n" + stderr;

                // Password must NEVER appear in stdout, stderr, or combined output
                Assert.DoesNotContain(TestSecretPassword, combinedOutput, StringComparison.OrdinalIgnoreCase);

                // No stack trace in output
                Assert.DoesNotContain("at RoleRadar.", combinedOutput, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("at MongoDB.", combinedOutput, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Exception:", combinedOutput, StringComparison.OrdinalIgnoreCase);

                // Fixed message must be printed
                Assert.Contains(ExpectedFixedErrorMessage, combinedOutput);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Theory]
    [MemberData(nameof(InvalidConnectionStringsWithSecret))]
    public async Task McpServer_WithInvalidConnectionString_NeverEchoesSecret_PrintsFixedErrorToStderrAndExitsNonZero(string invalidConnStr)
    {
        var mcpDll = GetMcpDllPath();

        var env = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = invalidConnStr,
            ["ROLERADAR_MONGO_DATABASE"] = "roleradar_test_db"
        };

        var (exitCode, stdout, stderr) = await RunProcessAsync(mcpDll, "", env);

        Assert.NotEqual(0, exitCode);

        // Password must NEVER appear in stdout, stderr or combined output
        Assert.DoesNotContain(TestSecretPassword, stdout, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TestSecretPassword, stderr, StringComparison.OrdinalIgnoreCase);

        // MCP server must not emit partial JSON-RPC output to stdout on composition failure
        Assert.DoesNotContain("\"jsonrpc\":", stdout, StringComparison.OrdinalIgnoreCase);

        // No stack trace
        Assert.DoesNotContain("at RoleRadar.", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at MongoDB.", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception:", stderr, StringComparison.OrdinalIgnoreCase);

        // Fixed error message must be on stderr
        Assert.Contains(ExpectedFixedErrorMessage, stderr);
    }

    [Fact]
    public async Task Cli_WithGarbageConnectionString_HelpAndVersionExitZero_WhileStatusFailsWithFixedMessage()
    {
        var cliDll = GetCliDllPath();
        var env = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = "garbage-value-with-secret-password-12345!"
        };

        // 1. --help exits 0 and prints help text
        var (helpExit, helpStdout, helpStderr) = await RunProcessAsync(cliDll, "--help", env);
        Assert.Equal(0, helpExit);
        Assert.Contains("RoleRadar local CLI", helpStdout);
        Assert.DoesNotContain("garbage-value-with-secret-password-12345!", helpStdout);
        Assert.DoesNotContain("garbage-value-with-secret-password-12345!", helpStderr);
        Assert.DoesNotContain(ExpectedFixedErrorMessage, helpStderr);

        // 2. --version exits 0
        var (versionExit, versionStdout, versionStderr) = await RunProcessAsync(cliDll, "--version", env);
        Assert.Equal(0, versionExit);
        Assert.DoesNotContain("garbage-value-with-secret-password-12345!", versionStdout);
        Assert.DoesNotContain("garbage-value-with-secret-password-12345!", versionStderr);
        Assert.DoesNotContain(ExpectedFixedErrorMessage, versionStderr);

        // 3. Unknown command exits non-zero without throwing Mongo configuration error
        var (unknownExit, unknownStdout, unknownStderr) = await RunProcessAsync(cliDll, "unknown-command-xyz", env);
        Assert.NotEqual(0, unknownExit);
        Assert.DoesNotContain("garbage-value-with-secret-password-12345!", unknownStdout);
        Assert.DoesNotContain("garbage-value-with-secret-password-12345!", unknownStderr);
        Assert.DoesNotContain(ExpectedFixedErrorMessage, unknownStderr);

        // 4. status command requires Mongo configuration: prints fixed message and exits 1 with no echo of secret
        var (statusExit, statusStdout, statusStderr) = await RunProcessAsync(cliDll, "status", env);
        Assert.Equal(1, statusExit);
        var combinedStatus = statusStdout + "\n" + statusStderr;
        Assert.DoesNotContain("garbage-value-with-secret-password-12345!", combinedStatus);
        Assert.DoesNotContain("at RoleRadar.", combinedStatus);
        Assert.DoesNotContain("at MongoDB.", combinedStatus);
        Assert.DoesNotContain("Exception:", combinedStatus);
        Assert.Contains(ExpectedFixedErrorMessage, combinedStatus);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string dllPath,
        string arguments,
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = string.IsNullOrWhiteSpace(arguments) ? $"\"{dllPath}\"" : $"\"{dllPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var (k, v) in environmentVariables)
        {
            psi.Environment[k] = v;
        }

        using var proc = Process.Start(psi)!;
        Assert.NotNull(proc);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (proc.ExitCode, stdout, stderr);
    }
}
