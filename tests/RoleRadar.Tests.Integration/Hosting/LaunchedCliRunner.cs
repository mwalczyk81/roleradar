using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

internal static class LaunchedCliRunner
{
    public static string GetCliDllPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        if (!File.Exists(path))
        {
            path = typeof(RoleRadar.Cli.Program).Assembly.Location;
        }

        Assert.True(File.Exists(path), $"Expected CLI assembly at '{path}'.");
        return path;
    }

    public static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(
        string arguments,
        IReadOnlyDictionary<string, string> env,
        bool? forceContainer = null)
    {
        var runInContainer = forceContainer ?? (Environment.GetEnvironmentVariable("ROLERADAR_TEST_IMAGE") == "1");
        if (runInContainer)
        {
            var image = Environment.GetEnvironmentVariable("ROLERADAR_CLI_IMAGE")
                ?? Environment.GetEnvironmentVariable("ROLERADAR_IMAGE_TAG")
                ?? "roleradar-cli:test";

            var runArgs = new StringBuilder("run -i --rm --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --cap-drop ALL --security-opt no-new-privileges --add-host host.docker.internal:host-gateway ");

            // Check if arguments contain a backup file path needing a mount
            var translatedArgs = arguments;
            var match = Regex.Match(arguments, @"(?:backup|restore)\s+""([^""]+)""");
            if (match.Success)
            {
                var hostPath = match.Groups[1].Value;
                var hostDir = Path.GetDirectoryName(hostPath);
                if (string.IsNullOrWhiteSpace(hostDir))
                {
                    hostDir = Path.GetTempPath();
                }

                var fileName = Path.GetFileName(hostPath);
                var containerDir = "/data/backup";
                var containerPath = $"{containerDir}/{fileName}";

                ContainerTestHarness.EnsureWritableDirectory(hostDir);
                runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"-v \"{hostDir}:{containerDir}:rw\" ");
                if (!string.IsNullOrEmpty(ContainerTestHarness.HostUserAndGroup))
                {
                    runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"--user \"{ContainerTestHarness.HostUserAndGroup}\" ");
                }

                translatedArgs = arguments.Replace($"\"{hostPath}\"", $"\"{containerPath}\"");
            }

            foreach (var (k, v) in env)
            {
                var val = v;
                if (k.Equals("ROLERADAR_MONGO_CONNECTION_STRING", StringComparison.OrdinalIgnoreCase))
                {
                    ContainerTestHarness.ValidateNotDevStack(v);
                    val = val.Replace("127.0.0.1", "host.docker.internal").Replace("localhost", "host.docker.internal");
                }
                runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"-e {k}=\"{val}\" ");
            }

            runArgs.Append(System.Globalization.CultureInfo.InvariantCulture, $"{image} {translatedArgs}");

            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = runArgs.ToString().Trim(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;
            Assert.NotNull(proc);

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            await proc.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return (proc.ExitCode, stdout, stderr);
        }
        else
        {
            var dllPath = GetCliDllPath();
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{dllPath}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var (k, v) in env)
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
}
