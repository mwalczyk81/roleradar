using System.Diagnostics;
using System.Globalization;

namespace RoleRadar.Tests.Integration.Backup;

internal sealed record PidWaitResult(bool PidFileExists, string Explanation);

/// <summary>
/// Waits for the child process of a backup to write its PID file, and watches the backup task while it waits. A backup
/// that ends before any child wrote the file (the dump command could not be started, an early exception) is reported at
/// once with its outcome, instead of after the whole budget with a message that looks like "the child was slow"
/// (feature 010, flake 4, research R14).
/// </summary>
internal static class BackupProcessWait
{
    public static async Task<PidWaitResult> WaitForPidFileAsync(string pidFile, Task backupTask, TimeSpan budget)
    {
        var elapsed = Stopwatch.StartNew();
        while (!File.Exists(pidFile) && !backupTask.IsCompleted && elapsed.Elapsed < budget)
        {
            await Task.Delay(50);
        }

        var ms = elapsed.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
        if (File.Exists(pidFile))
        {
            return new PidWaitResult(true, $"PID file seen after {ms} ms");
        }

        if (backupTask.IsFaulted)
        {
            return new PidWaitResult(false, $"the backup task ended after {ms} ms before any child wrote the PID file, faulted: {backupTask.Exception!.GetBaseException()}");
        }

        if (backupTask.IsCanceled)
        {
            return new PidWaitResult(false, $"the backup task ended after {ms} ms before any child wrote the PID file, cancelled");
        }

        if (backupTask.IsCompleted)
        {
            return new PidWaitResult(false, $"the backup task completed after {ms} ms without any child having written the PID file");
        }

        return new PidWaitResult(false, $"no PID file after {ms} ms (budget {budget.TotalMilliseconds:F0} ms) and the backup task is still {backupTask.Status}");
    }
}
