using System.Text.RegularExpressions;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>
/// Feature 014 US2 (FR-025, contracts/trace-context.md): a CLI run has one trace id, 32 lower-case hex characters, the same
/// for every log record and audit entry the run writes. The per-call MCP id is covered end to end by
/// <c>HostLogFormatIntegrationTests</c> (a distinct 32-hex id per tool call, in the records the host writes to stderr).
/// </summary>
public sealed class RunIdTests
{
    [Fact]
    public void TheCliRunId_Is32HexCharacters_AndStableWithinTheRun()
    {
        var first = RoleRadar.Cli.Program.RunId;
        var second = RoleRadar.Cli.Program.RunId;

        Assert.Matches(new Regex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant), first);
        Assert.NotEqual(new string('0', 32), first);
        Assert.Equal(first, second);
    }
}
