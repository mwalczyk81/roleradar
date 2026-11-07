using System.Diagnostics;
using Xunit;

namespace RoleRadar.Tests.Integration.Backup;

/// <summary>
/// Feature 010, flake 4 (research R14). <c>Backup_WhenCancelled_TerminatesEntireProcessTree</c> failed once with only
/// "Expected child PID file to be created by test process." after its 5 s wait, which cannot tell a slow child process
/// from a backup that ended before it ever launched one. Measured: the child needs 67 ms at idle and at most 489 ms under
/// extreme CPU load, so a slow start does not explain a 5 s miss. The wait now watches the backup task too and says why.
/// Docker-free: the "backup" is a plain Task.
/// </summary>
public sealed class BackupProcessWaitTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rr-pidwait-" + Guid.NewGuid().ToString("N"));

    public BackupProcessWaitTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string PidFile => Path.Combine(_dir, "child_pid.txt");

    [Fact]
    public async Task APidFileThatAppears_IsReported_AsSoonAsItExists()
    {
        var backup = Task.Delay(Timeout.Infinite);
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            await File.WriteAllTextAsync(PidFile, "123");
        });

        var watch = Stopwatch.StartNew();
        var result = await BackupProcessWait.WaitForPidFileAsync(PidFile, backup, TimeSpan.FromSeconds(10));

        Assert.True(result.PidFileExists);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "it must not wait out the budget once the file exists");
    }

    [Fact]
    public async Task ABackupThatFaultsBeforeTheChildWrites_IsReportedAtOnce_WithItsException()
    {
        var backup = Task.FromException(new InvalidOperationException("dump command could not be started: simulated"));

        var watch = Stopwatch.StartNew();
        var result = await BackupProcessWait.WaitForPidFileAsync(PidFile, backup, TimeSpan.FromSeconds(10));

        Assert.False(result.PidFileExists);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "an ended backup must not be waited on for the whole budget");
        Assert.Contains("ended", result.Explanation);
        Assert.Contains("dump command could not be started: simulated", result.Explanation);
    }

    [Fact]
    public async Task ABackupThatCompletesWithoutALaunchedChild_IsReported_AsNotHavingLaunchedOne()
    {
        var result = await BackupProcessWait.WaitForPidFileAsync(PidFile, Task.CompletedTask, TimeSpan.FromSeconds(10));

        Assert.False(result.PidFileExists);
        Assert.Contains("completed", result.Explanation);
        Assert.Contains("without", result.Explanation);
    }

    [Fact]
    public async Task ABackupStillRunningAtTheBudget_IsReported_WithTheElapsedTimeAndTaskStatus()
    {
        var backup = Task.Delay(Timeout.Infinite);

        var result = await BackupProcessWait.WaitForPidFileAsync(PidFile, backup, TimeSpan.FromMilliseconds(400));

        Assert.False(result.PidFileExists);
        Assert.Contains("still", result.Explanation);
        Assert.Contains("after", result.Explanation);
        Assert.Contains("ms", result.Explanation);
    }
}
