using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>
/// One test per rule in research R-003, so removing any single rule fails exactly one case (SC-003).
/// All values are fictional.
/// </summary>
public sealed class LogScrubberTests
{
    private const string Jwt = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIwMTkyIn0.c2lnbmF0dXJlLWZpY3Rpb25hbA"; // gitleaks:allow (fictional redaction test vector, not a credential)

    [Theory]
    [InlineData("Authorization: Bearer abc.def-ghi_123", "abc.def-ghi_123")]
    [InlineData("header was bearer SENTINEL-TOKEN-7f3a now", "SENTINEL-TOKEN-7f3a")]
    public void Rule_BearerCredential_IsRedacted(string input, string secret)
    {
        var output = LogScrubber.ScrubText(input);

        Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        Assert.Contains(LogScrubber.Redacted, output, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_BasicCredential_IsRedacted()
    {
        var output = LogScrubber.ScrubText("Authorization: Basic cnItdXNlcjpTRU5USU5FTA==");

        Assert.DoesNotContain("cnItdXNlcjpTRU5USU5FTA==", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_BareJwt_IsRedacted()
    {
        var output = LogScrubber.ScrubText($"token issued {Jwt} for client");

        Assert.DoesNotContain(Jwt, output, StringComparison.Ordinal);
        Assert.StartsWith("token issued ", output, StringComparison.Ordinal);
        Assert.EndsWith(" for client", output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SecretVectors.MongoConnectionStrings), MemberType = typeof(SecretVectors))]
    public void Rule_MongoUserInfo_IsRedacted(string input)
    {
        var output = LogScrubber.ScrubText(input);

        Assert.DoesNotContain(SecretVectors.Password, output, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretVectors.User, output, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SecretVectors.MongoConnectionStrings), MemberType = typeof(SecretVectors))]
    public void SharedVectors_AreAlsoRedactedByConnectionStringRedactor(string input)
    {
        // The same vectors through the Infrastructure redactor: both rules must agree on what a secret is.
        var output = ConnectionStringRedactor.RedactSensitiveText(input);

        Assert.DoesNotContain(SecretVectors.Password, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("passphrase")]
    [InlineData("secret")]
    [InlineData("client_secret")]
    [InlineData("token")]
    [InlineData("accessToken")]
    [InlineData("refresh_token")]
    [InlineData("authorization")]
    [InlineData("Authorization")]
    [InlineData("cookie")]
    [InlineData("set-cookie")]
    [InlineData("otp")]
    [InlineData("totp")]
    [InlineData("TOTP")]
    [InlineData("code")]
    [InlineData("body")]
    [InlineData("content")]
    [InlineData("appendedText")]
    [InlineData("noteBody")]
    [InlineData("state")]
    [InlineData("nonce")]
    [InlineData("code_challenge")]
    [InlineData("code_verifier")]
    [InlineData("id_token_hint")]
    public void Rule_DenylistedKey_ValueIsRedacted(string key)
    {
        Assert.True(LogScrubber.IsSensitiveKey(key), $"'{key}' must be treated as sensitive.");
        Assert.Equal(LogScrubber.Redacted, LogScrubber.ScrubProperty(key, "SENTINEL-VALUE-7f3a"));
    }

    [Theory]
    [InlineData("statusCode")]
    [InlineData("method")]
    [InlineData("path")]
    [InlineData("durationMs")]
    [InlineData("subject")]
    [InlineData("correlationId")]
    [InlineData("entityType")]
    public void OrdinaryKeys_AreNotSensitive(string key)
    {
        Assert.False(LogScrubber.IsSensitiveKey(key));
        Assert.Equal("ordinary", LogScrubber.ScrubProperty(key, "ordinary"));
    }

    [Theory]
    [InlineData("GET https://identity.test/auth/callback?code=abc&state=xyz done", "GET https://identity.test/auth/callback done")]
    [InlineData("/auth/callback?code=abc&state=xyz", "/auth/callback")]
    [InlineData("redirect to /connect/authorize?client_id=roleradar-web&code_challenge=q", "redirect to /connect/authorize")]
    public void Rule_QueryString_IsStrippedFromUrlShapedValues(string input, string expected)
    {
        Assert.Equal(expected, LogScrubber.ScrubText(input));
    }

    [Theory]
    [InlineData("invalid_grant refresh_token=SENTINEL-REFRESH-7f3a rejected", "SENTINEL-REFRESH-7f3a")]
    [InlineData("client_secret=SENTINEL-CS-7f3a&grant_type=x", "SENTINEL-CS-7f3a")]
    [InlineData("login failed password=SENTINEL-PW-7f3a", "SENTINEL-PW-7f3a")] // gitleaks:allow (fictional redaction test vector, not a credential)
    [InlineData("otp=123456 rejected", "123456")]
    public void Rule_SensitiveKeyValuePair_InFreeText_IsRedacted(string input, string secret)
    {
        var output = LogScrubber.ScrubText(input);

        Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        Assert.Contains(LogScrubber.Redacted, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\r\n  \"State\": \"SENTINEL-STATE\",\r\n  \"ClientId\": \"roleradar-web\"\r\n}", "SENTINEL-STATE", "roleradar-web")]
    [InlineData("\"Raw\": { \"code_challenge\": \"SENTINEL-CHALLENGE\", \"scope\": \"openid\" }", "SENTINEL-CHALLENGE", "openid")]
    [InlineData("{\"client_secret\":\"SENTINEL-CS\",\"grant_type\":\"authorization_code\"}", "SENTINEL-CS", "authorization_code")]
    public void Rule_JsonPairWithSensitiveKey_IsRedacted(string input, string secret, string kept)
    {
        var output = LogScrubber.ScrubText(input);

        Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
        Assert.Contains(kept, output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Now listening on: http://127.0.0.1:8080")]
    [InlineData("Ready?")]
    [InlineData("Is the store ready? yes")]
    [InlineData("Posting 0197a1b2-0000-7000-8000-000000000001 transitioned to screened_out")]
    [InlineData("")]
    public void ValuesWithoutSecrets_AreByteIdentical(string input)
    {
        Assert.Equal(input, LogScrubber.ScrubText(input));
    }

    [Fact]
    public void Null_BecomesEmpty()
    {
        Assert.Equal(string.Empty, LogScrubber.ScrubText(null));
    }
}
