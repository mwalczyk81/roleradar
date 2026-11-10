using RoleRadar.Cli.Commands;
using Xunit;

namespace RoleRadar.Tests.Unit.Cli;

/// <summary>
/// Feature 013 (FR-020): <c>audit list</c> text output replaces anything that could move the cursor, colour the screen,
/// start a second line or reorder what the owner reads. This is tested on the function itself because a redirected
/// Windows console encodes unmappable characters as "?" on its own, which would hide a missing neutralization.
/// The characters are given as code points so the source stays plain ASCII (no invisible or bidirectional text in it).
/// </summary>
public sealed class AuditPrintableTests
{
    private static string Around(int codePoint) => $"a{char.ConvertFromUtf32(codePoint)}b";

    [Theory]
    [InlineData(0x202E)] // right-to-left override (Cf)
    [InlineData(0x202D)] // left-to-right override (Cf)
    [InlineData(0x202A)] // left-to-right embedding (Cf)
    [InlineData(0x200B)] // zero-width space (Cf)
    [InlineData(0x200F)] // right-to-left mark (Cf)
    [InlineData(0x2066)] // left-to-right isolate (Cf)
    [InlineData(0x2069)] // pop directional isolate (Cf)
    [InlineData(0xFEFF)] // byte order mark, zero-width no-break space (Cf)
    public void FormatCharacters_BecomeQuestionMarks(int codePoint) =>
        Assert.Equal("a?b", AuditCommand.Printable(Around(codePoint)));

    [Theory]
    [InlineData(0x001B)] // escape (Cc)
    [InlineData(0x0007)] // bell (Cc)
    [InlineData(0x000A)] // line feed (Cc)
    [InlineData(0x000D)] // carriage return (Cc)
    [InlineData(0x007F)] // delete (Cc)
    [InlineData(0x0085)] // next line (Cc)
    [InlineData(0x2028)] // line separator (Zl)
    [InlineData(0x2029)] // paragraph separator (Zp)
    public void ControlAndSeparatorCharacters_StillBecomeQuestionMarks(int codePoint) =>
        Assert.Equal("a?b", AuditCommand.Printable(Around(codePoint)));

    [Fact]
    public void SeveralHazards_InOneValue_AreEachReplaced() =>
        Assert.Equal(
            "ann?evil?actor?",
            AuditCommand.Printable($"ann{(char)0x202E}evil{(char)0x200B}actor{(char)0x2066}"));

    [Theory]
    [InlineData("cli-user")]
    [InlineData("0196f3c2-7b1e-7000-8000-00000000abcd")]
    [InlineData("req-0001 with spaces")]
    public void OrdinaryText_IsLeftAlone(string stored) =>
        Assert.Equal(stored, AuditCommand.Printable(stored));

    [Fact]
    public void OrdinaryNonAsciiLetters_AreLeftAlone()
    {
        var stored = $"caf{(char)0x00E9} {(char)0x65E5}{(char)0x672C}";

        Assert.Equal(stored, AuditCommand.Printable(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyValues_PrintAsADash(string? stored) =>
        Assert.Equal("-", AuditCommand.Printable(stored));
}
