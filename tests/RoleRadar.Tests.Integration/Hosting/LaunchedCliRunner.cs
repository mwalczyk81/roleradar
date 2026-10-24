using System.Diagnostics;
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
            throw new NotImplementedException("Container CLI execution in LaunchedCliRunner is not yet implemented (T027).");
        }

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
