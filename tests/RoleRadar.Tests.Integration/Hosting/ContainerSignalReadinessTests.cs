using System.Text.RegularExpressions;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

/// <summary>
/// Docker-free guards for the SIGTERM readiness probe (feature 010, user story 3, research R8). A signal sent to a
/// container's PID 1 before the process can catch it is dropped, because the kernel gives PID 1 no default action. The
/// SIGTERM tests therefore wait until <c>SigCgt</c> in <c>/proc/1/status</c> shows the handler, instead of signalling
/// right after <c>docker run -d</c> returns.
/// </summary>
[Trait("Lane", "default")]
public sealed class ContainerSignalReadinessTests
{
    // A real /proc/1/status excerpt from the CLI image once its runtime was up (SIGTERM is signal 15, bit 14, 0x4000).
    private const string Caught = "Name:\tRoleRadar.Cli\nSigPnd:\t0000000000000000\nSigBlk:\t0000000000000000\nSigIgn:\t0000000000001000\nSigCgt:\t00000003000244fe\n";

    [Fact]
    public void ARealStatusWithTheSigtermBitSet_IsReady()
    {
        Assert.True(ContainerSignalReadiness.IsSigtermCaught(Caught));
    }

    [Theory]
    [InlineData("SigCgt:\t0000000000000000")]
    [InlineData("SigCgt:\t0000000000001000")]
    [InlineData("SigCgt:\t0000000000008000")]
    [InlineData("SigCgt:\t0000000000002000")]
    public void AMaskWithoutBit14_IsNotReady(string line)
    {
        Assert.False(ContainerSignalReadiness.IsSigtermCaught("Name:\tdotnet\n" + line + "\n"));
    }

    [Theory]
    [InlineData("SigCgt:\t0000000000004000")]
    [InlineData("SigCgt: 4000")]
    [InlineData("SigCgt:\tFFFFFFFFFFFFFFFF")]
    public void AMaskWithBit14_IsReady(string line)
    {
        Assert.True(ContainerSignalReadiness.IsSigtermCaught("Name:\tdotnet\n" + line + "\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Name:\tdotnet\nSigIgn:\t0000000000004000\n")]
    [InlineData("SigCgt:\tnot-hex\n")]
    [InlineData("SigCgt:\t\n")]
    public void MissingOrUnreadableSigCgt_IsNotReady_AndNeverThrows(string status)
    {
        Assert.False(ContainerSignalReadiness.IsSigtermCaught(status));
    }

    [Fact]
    public void TheHelperImageIsPinnedToTheSameDigestAsTheDockerfile()
    {
        var dockerfile = File.ReadAllText(Path.Combine(ContainerTestHarness.FindRepoRoot(), "Dockerfile"));
        var match = Regex.Match(dockerfile, @"FROM (ubuntu:[\w.]+@sha256:[0-9a-f]{64}) AS tools-downloader");

        Assert.True(match.Success, "the Dockerfile tools-downloader stage no longer names a pinned ubuntu image");
        Assert.Equal(ContainerSignalReadiness.HelperImage, match.Groups[1].Value);
    }
}
