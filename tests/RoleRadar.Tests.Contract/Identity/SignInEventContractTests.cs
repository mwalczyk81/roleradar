using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

/// <summary>
/// Feature 013 US2 (FR-018, FR-019, SC-005, SC-011): each sign-in outcome writes exactly one structured event with the
/// request's correlation id, carrying <c>subject</c> only when an account matched, and no line contains the typed
/// username, the password or the one-time code. Logs only: nothing is stored. All values are fictional.
/// </summary>
public sealed partial class SignInEventContractTests
{
    private const string OwnerUsername = "testowner";
    private const string OwnerPassword = "Password123!";
    private const string WrongPassword = "SENTINEL-WRONG-PW-4c2d"; // gitleaks:allow (fictional test vector, not a credential)
    private const string UnknownUsername = "SENTINEL-UNKNOWN-USER-4c2d";
    private const string WrongCode = "000000";

    private const int SignInSucceeded = 5001;
    private const int SignInFailed = 5002;
    private const int AccountLockedOut = 5003;
    private const int OneTimeCodeFailed = 5004;

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*?value=""([^""]+)""")]
    private static partial Regex AntiForgery();

    private sealed record Event(int EventId, string Name, string Level, string? CorrelationId, JsonElement Props);

    private static FormatterLogCapture NewCapture() => new(RoleRadarConsoleFormatter.Create("identity", LogFormat.Json));

    private static async Task<string> TokenAsync(HttpClient client) =>
        AntiForgery().Match(await client.GetStringAsync("/Account/Login")).Groups[1].Value;

    private static async Task<HttpResponseMessage> PostPasswordAsync(HttpClient client, string username, string password)
    {
        var token = await TokenAsync(client);
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/",
            ["RequiresTwoFactor"] = "false",
            ["Input.Username"] = username,
            ["Input.Password"] = password,
        }));
    }

    private static async Task<HttpResponseMessage> PostCodeAsync(HttpClient client, string passwordStepHtml, string code)
    {
        var token = AntiForgery().Match(passwordStepHtml).Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "the code step did not render an anti-forgery token");
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/",
            ["RequiresTwoFactor"] = "true",
            ["TwoFactorInput.Code"] = code,
        }));
    }

    private static (Guid SubjectId, string CurrentCode) OwnerFacts(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
        var user = db.Users.Single(u => u.UserName == OwnerUsername);
        var key = db.UserTokens.Single(t => t.UserId == user.Id && t.Name == "AuthenticatorKey").Value!;
        // A code already accepted is rejected as a replay (ADR 0042), so every call takes a code not handed out before.
        return (user.Id, TotpTestCodes.NextUnused(key));
    }

    private static List<Event> Events(FormatterLogCapture capture)
    {
        var events = new List<Event>();
        foreach (var line in capture.Lines)
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("eventId", out var idElement) || idElement.GetInt32() is < SignInSucceeded or > OneTimeCodeFailed)
            {
                continue;
            }

            var props = root.TryGetProperty("props", out var p) ? p.Clone() : default;
            events.Add(new Event(
                idElement.GetInt32(),
                root.GetProperty("event").GetString()!,
                root.GetProperty("level").GetString()!,
                root.TryGetProperty("correlationId", out var c) ? c.GetString() : null,
                props));
        }

        return events;
    }

    /// <summary>
    /// The capture must not be vacuous: lines exist, the login request's RequestCompleted record is among them, and the
    /// expected security event is present. Every event and no-secret check runs only after this.
    /// </summary>
    private static void AssertCaptureIsNotVacuous(FormatterLogCapture capture, string expectedEvent)
    {
        Assert.NotEmpty(capture.Lines);
        Assert.Contains(capture.Lines, line =>
            line.Contains("\"event\":\"RequestCompleted\"", StringComparison.Ordinal)
            && line.Contains("\"path\":\"/Account/Login\"", StringComparison.Ordinal));
        Assert.Contains(Events(capture), e => e.Name == expectedEvent);
    }

    private static string? SubjectOf(Event e) =>
        e.Props.ValueKind == JsonValueKind.Object && e.Props.TryGetProperty("subject", out var s) ? s.GetString() : null;

    private static bool HasSubjectKey(Event e) =>
        e.Props.ValueKind == JsonValueKind.Object && e.Props.TryGetProperty("subject", out _);

    private static void AssertEveryEventCorrelated(IEnumerable<Event> events) =>
        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.CorrelationId), $"{e.Name} has no correlationId"));

    private static void AssertNoneOfTheSecretsAppear(FormatterLogCapture capture, params string[] secrets)
    {
        var text = capture.Text;
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<T> WithHostAsync<T>(FormatterLogCapture capture, Func<HttpClient, IServiceProvider, Task<T>> body)
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(builder => builder.Logging.AddProvider(capture));
        try
        {
            return await body(client, app.Services);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            try
            {
                Directory.Delete(dbDir, recursive: true);
            }
            catch (IOException)
            {
                // best effort: SQLite may still hold the file briefly
            }
        }
    }

    [Fact]
    public async Task WrongPassword_ForAnExistingUser_WritesOneSignInFailed_WithTheSubject()
    {
        var capture = NewCapture();
        var subject = await WithHostAsync(capture, async (client, services) =>
        {
            var response = await PostPasswordAsync(client, OwnerUsername, WrongPassword);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return OwnerFacts(services).SubjectId;
        });

        AssertCaptureIsNotVacuous(capture, "SignInFailed");
        var events = Events(capture);
        var only = Assert.Single(events);
        Assert.Equal(SignInFailed, only.EventId);
        Assert.Equal("warn", only.Level);
        Assert.Equal(subject.ToString("D"), SubjectOf(only));
        AssertEveryEventCorrelated(events);
        AssertNoneOfTheSecretsAppear(capture, OwnerUsername, WrongPassword, OwnerPassword);
    }

    [Fact]
    public async Task UnknownUsername_WritesOneSignInFailed_WithNoSubject()
    {
        var capture = NewCapture();
        await WithHostAsync(capture, async (client, _) =>
        {
            var response = await PostPasswordAsync(client, UnknownUsername, WrongPassword);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return 0;
        });

        AssertCaptureIsNotVacuous(capture, "SignInFailed");
        var events = Events(capture);
        var only = Assert.Single(events);
        Assert.Equal(SignInFailed, only.EventId);
        Assert.Equal("warn", only.Level);
        Assert.False(HasSubjectKey(only), "an unknown username must not produce a subject property, not even a null one");
        AssertEveryEventCorrelated(events);
        AssertNoneOfTheSecretsAppear(capture, UnknownUsername, WrongPassword, OwnerPassword);
    }

    [Fact]
    public async Task CorrectPassword_WithAWrongCode_WritesOneOneTimeCodeFailed_WithTheSubject()
    {
        var capture = NewCapture();
        var (subject, usedWrongCode) = await WithHostAsync(capture, async (client, services) =>
        {
            var facts = OwnerFacts(services);
            var wrongCode = facts.CurrentCode == WrongCode ? "111111" : WrongCode;
            var passwordStep = await PostPasswordAsync(client, OwnerUsername, OwnerPassword);
            Assert.Equal(HttpStatusCode.OK, passwordStep.StatusCode);
            var codeStep = await PostCodeAsync(client, await passwordStep.Content.ReadAsStringAsync(), wrongCode);
            Assert.Equal(HttpStatusCode.OK, codeStep.StatusCode);
            return (facts.SubjectId, wrongCode);
        });

        AssertCaptureIsNotVacuous(capture, "OneTimeCodeFailed");
        var events = Events(capture);
        var only = Assert.Single(events);
        Assert.Equal(OneTimeCodeFailed, only.EventId);
        Assert.Equal("warn", only.Level);
        Assert.Equal(subject.ToString("D"), SubjectOf(only));
        AssertEveryEventCorrelated(events);
        AssertNoneOfTheSecretsAppear(capture, OwnerUsername, OwnerPassword, usedWrongCode);
    }

    [Fact]
    public async Task CorrectPasswordAndCode_WriteOneSignInSucceeded_WithTheSubject()
    {
        var capture = NewCapture();
        var (subject, code) = await WithHostAsync(capture, async (client, services) =>
        {
            var facts = OwnerFacts(services);
            var passwordStep = await PostPasswordAsync(client, OwnerUsername, OwnerPassword);
            Assert.Equal(HttpStatusCode.OK, passwordStep.StatusCode);
            var codeStep = await PostCodeAsync(client, await passwordStep.Content.ReadAsStringAsync(), facts.CurrentCode);
            Assert.Equal(HttpStatusCode.Redirect, codeStep.StatusCode);
            return (facts.SubjectId, facts.CurrentCode);
        });

        AssertCaptureIsNotVacuous(capture, "SignInSucceeded");
        var events = Events(capture);
        var only = Assert.Single(events);
        Assert.Equal(SignInSucceeded, only.EventId);
        Assert.Equal("info", only.Level);
        Assert.Equal(subject.ToString("D"), SubjectOf(only));
        AssertEveryEventCorrelated(events);
        AssertNoneOfTheSecretsAppear(capture, OwnerUsername, OwnerPassword, code);
    }

    [Fact]
    public async Task TheFifthConsecutiveFailure_WritesSignInFailed_ThenAccountLockedOut()
    {
        var capture = NewCapture();
        var subject = await WithHostAsync(capture, async (client, services) =>
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                var response = await PostPasswordAsync(client, OwnerUsername, WrongPassword);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            return OwnerFacts(services).SubjectId;
        });

        AssertCaptureIsNotVacuous(capture, "AccountLockedOut");
        var events = Events(capture);
        Assert.Equal(6, events.Count);
        Assert.All(events.Take(5), e => Assert.Equal(SignInFailed, e.EventId));
        Assert.Equal(AccountLockedOut, events[5].EventId);
        Assert.Equal("warn", events[5].Level);
        Assert.All(events, e => Assert.Equal(subject.ToString("D"), SubjectOf(e)));
        AssertEveryEventCorrelated(events);
        AssertNoneOfTheSecretsAppear(capture, OwnerUsername, WrongPassword, OwnerPassword);
    }
}
