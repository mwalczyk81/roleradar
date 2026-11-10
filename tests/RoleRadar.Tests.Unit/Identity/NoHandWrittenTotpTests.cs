using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

/// <summary>
/// Feature 014 (ADR 0042, FR-010, SC-003): the shipped source contains no hand-written RFC 6238 or Base32 code, and the
/// sign-in page verifies a code only through the guarded verifier.
/// </summary>
public sealed class NoHandWrittenTotpTests
{
    private static readonly string[] Forbidden = ["HMACSHA1", "ComputeTotpForStep", "ComputeTotpCode", "Base32Decode", "Base32Encode"];

    [Fact]
    public void ShippedSource_HasNoHandWrittenRfc6238OrBase32()
    {
        var sourceFiles = SourceFiles().ToList();

        // The scan must not pass vacuously.
        Assert.True(sourceFiles.Count >= 50, $"The scan read only {sourceFiles.Count} source files.");

        var hits = new List<string>();
        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            hits.AddRange(Forbidden.Where(token => text.Contains(token, StringComparison.Ordinal)).Select(token => $"{Path.GetRelativePath(FindRepoRoot(), file)}: {token}"));
        }

        Assert.True(hits.Count == 0, "Hand-written TOTP or Base32 code remains:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    [Fact]
    public void SignInPage_DoesNotVerifyACodeAnywhereExceptThroughTheGuardedVerifier()
    {
        var page = Path.Combine(FindRepoRoot(), "src", "RoleRadar.Identity", "Pages", "Account", "Login.cshtml.cs");
        Assert.True(File.Exists(page), $"Expected '{page}' to exist.");

        // SignInManager.TwoFactorAuthenticatorSignInAsync goes straight to the platform provider, which accepts a code more
        // than once: the page must not call it (the fallback branch used to).
        Assert.DoesNotContain("TwoFactorAuthenticatorSignInAsync", File.ReadAllText(page), StringComparison.Ordinal);
    }

    private static IEnumerable<string> SourceFiles()
    {
        var src = Path.Combine(FindRepoRoot(), "src");
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(src, path).Replace('\\', '/');
                return !relative.Contains("/obj/", StringComparison.Ordinal)
                    && !relative.Contains("/bin/", StringComparison.Ordinal)
                    && !relative.Contains("/Migrations/", StringComparison.Ordinal);
            });
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

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
