using FsCheck;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class NormalizerPropertyAndUnitTests
{
    [Theory]
    [InlineData("https://boards.greenhouse.io/acmesoftware/jobs/4012948002?gh_src=linkedin", "https://boards.greenhouse.io/acmesoftware/jobs/4012948002")]
    [InlineData("HTTPS://BOARDS.GREENHOUSE.IO:443/acmesoftware/jobs/4012948002/", "https://boards.greenhouse.io/acmesoftware/jobs/4012948002")]
    [InlineData("http://example.com:80/jobs/?utm_source=twitter&utm_medium=social&utm_campaign=spring", "http://example.com/jobs")]
    [InlineData("https://jobs.example.com/role?req_id=999&fbclid=abc123xyz&ref=search", "https://jobs.example.com/role?req_id=999")]
    [InlineData("https://jobs.example.com/view?gclid=123&msclkid=456&trk=feed&source_id=indeed", "https://jobs.example.com/view")]
    [InlineData("https://company.com/", "https://company.com")]
    public void UrlNormalizer_StripsTrackingParams_CanonicalizesHostAndDefaultPort_RemovesTrailingSlash(string input, string expected)
    {
        var actual = UrlNormalizer.Normalize(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Senior Software Engineer - Backend (.NET)", "senior software engineer backend net")]
    [InlineData("  STAFF   ENGINEER,  PLATFORM  ", "staff engineer platform")]
    [InlineData("Lead Developer (Remote / Hybrid)!!!", "lead developer remote hybrid")]
    [InlineData("Principal Engineer #1", "principal engineer 1")]
    public void TitleNormalizer_Lowercases_StripsPunctuation_FoldsWhitespace(string input, string expected)
    {
        var actual = TitleNormalizer.Normalize(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void PropertyTest_UrlNormalizer_IsIdempotent()
    {
        var property = FsCheck.Fluent.Prop.ForAll<string>(raw =>
        {
            if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(raw, UriKind.Absolute, out _))
            {
                return true;
            }

            var once = UrlNormalizer.Normalize(raw);
            var twice = UrlNormalizer.Normalize(once);
            return string.Equals(once, twice, StringComparison.Ordinal);
        });

        property.QuickCheckThrowOnFailure();
    }

    [Fact]
    public void PropertyTest_TitleNormalizer_IsIdempotent()
    {
        var property = FsCheck.Fluent.Prop.ForAll<string>(raw =>
        {
            if (raw is null)
            {
                return true;
            }

            var once = TitleNormalizer.Normalize(raw);
            var twice = TitleNormalizer.Normalize(once);
            return string.Equals(once, twice, StringComparison.Ordinal);
        });

        property.QuickCheckThrowOnFailure();
    }
}
