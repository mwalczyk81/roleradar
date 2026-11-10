using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

// Feature 014 (ADR 0042, FR-010 to FR-016): the second-factor contract of the sign-in page. A partial of the 013 class so it
// reuses its page helpers and event parsing. Every case first checks that log lines were captured. All values are fictional.
public sealed partial class SignInEventContractTests
{
    private static int _nextLoopbackOctet = 1;

    private static readonly int[] AcceptedOffsets = [-2, -1, 0, 1, 2];
    private static readonly int[] RejectedOffsets = [-3, 3];
    private static readonly string[] WrongCodeCandidates = ["000000", "111111", "222222"];
    private static readonly int[] ReplayEventSequence = [SignInSucceeded, OneTimeCodeFailed];

    /// <summary>
    /// A client with its own cookie jar and its own loopback source address (127.0.0.2 to 127.0.0.251): the sign-in pages are
    /// limited to 10 requests a minute per address, and these tests make more requests than that.
    /// </summary>
    private static (HttpClient Client, CookieContainer Cookies) NewClientLike(HttpClient template)
    {
        var octet = (Interlocked.Increment(ref _nextLoopbackOctet) % 250) + 2;
        var local = IPAddress.Parse($"127.0.0.{octet}");
        var cookies = new CookieContainer();
        var handler = new SocketsHttpHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AllowAutoRedirect = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    socket.Bind(new IPEndPoint(local, 0));
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        return (new HttpClient(handler) { BaseAddress = template.BaseAddress }, cookies);
    }

    private static async Task<T> WithOwnerHostAsync<T>(
        FormatterLogCapture capture,
        OwnerSecondFactor secondFactor,
        Func<HttpClient, IServiceProvider, Task<T>> body)
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(
            builder => builder.Logging.AddProvider(capture),
            secondFactor);
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

    private static (Guid Id, string Key) OwnerKey(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
        var user = db.Users.Single(u => u.UserName == OwnerUsername);
        return (user.Id, db.UserTokens.Single(t => t.UserId == user.Id && t.Name == "AuthenticatorKey").Value!);
    }

    private static (int AccessFailedCount, bool LockedOut) OwnerLockState(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
        var user = db.Users.Single(u => u.UserName == OwnerUsername);
        return (user.AccessFailedCount, user.LockoutEnd > DateTimeOffset.UtcNow);
    }

    /// <summary>A code that is not valid in any of the five steps the platform provider accepts.</summary>
    private static string WrongCodeFor(string key)
    {
        var valid = AcceptedOffsets.Select(offset => TotpTestCodes.Current(key, offset)).ToHashSet(StringComparer.Ordinal);
        return WrongCodeCandidates.First(candidate => !valid.Contains(candidate));
    }

    /// <summary>One whole sign-in on a fresh session: the password step, then the code step. Returns the code step's status and page.</summary>
    private static async Task<(HttpStatusCode Status, string Html)> SignInAsync(HttpClient template, string code)
    {
        var (client, _) = NewClientLike(template);
        using (client)
        {
            var passwordStep = await PostPasswordAsync(client, OwnerUsername, OwnerPassword);
            Assert.Equal(HttpStatusCode.OK, passwordStep.StatusCode);
            var codeStep = await PostCodeAsync(client, await passwordStep.Content.ReadAsStringAsync(), code);
            return (codeStep.StatusCode, await codeStep.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task ReplayAtSignIn_ASecondUseOfAnAcceptedCode_IsRejected_WithOneOneTimeCodeFailed_AndOneFailedAttempt()
    {
        var capture = NewCapture();
        var (id, key, code, state) = await WithOwnerHostAsync(capture, OwnerSecondFactor.EnabledWithoutCode, async (client, services) =>
        {
            var (ownerId, ownerKey) = OwnerKey(services);
            var accepted = TotpTestCodes.NextUnused(ownerKey);

            var first = await SignInAsync(client, accepted);
            Assert.Equal(HttpStatusCode.Redirect, first.Status);

            var second = await SignInAsync(client, accepted);
            Assert.Equal(HttpStatusCode.OK, second.Status);
            Assert.Contains("Invalid verification code.", second.Html, StringComparison.Ordinal);

            return (ownerId, ownerKey, accepted, OwnerLockState(services));
        });

        AssertCaptureIsNotVacuous(capture, "OneTimeCodeFailed");
        var events = Events(capture);
        Assert.Equal(ReplayEventSequence, events.Select(e => e.EventId).ToArray());
        Assert.All(events, e => Assert.Equal(id.ToString("D"), SubjectOf(e)));
        Assert.Equal(1, state.AccessFailedCount);
        AssertNoneOfTheSecretsAppear(capture, OwnerUsername, OwnerPassword, code, key);
    }

    [Fact]
    public async Task TwoSimultaneousSubmissionsOfTheSameCode_OnlyOneSignsIn()
    {
        var capture = NewCapture();
        var (key, code, statuses) = await WithOwnerHostAsync(capture, OwnerSecondFactor.EnabledWithoutCode, async (client, services) =>
        {
            var (_, ownerKey) = OwnerKey(services);
            var accepted = TotpTestCodes.NextUnused(ownerKey);
            var (clientOne, _) = NewClientLike(client);
            var (clientTwo, _) = NewClientLike(client);
            using (clientOne)
            using (clientTwo)
            {
                var stepOne = await PostPasswordAsync(clientOne, OwnerUsername, OwnerPassword);
                var stepTwo = await PostPasswordAsync(clientTwo, OwnerUsername, OwnerPassword);
                var htmlOne = await stepOne.Content.ReadAsStringAsync();
                var htmlTwo = await stepTwo.Content.ReadAsStringAsync();

                var responses = await Task.WhenAll(PostCodeAsync(clientOne, htmlOne, accepted), PostCodeAsync(clientTwo, htmlTwo, accepted));
                return (ownerKey, accepted, responses.Select(r => r.StatusCode).ToArray());
            }
        });

        AssertCaptureIsNotVacuous(capture, "SignInSucceeded");
        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.Redirect));
        Assert.Equal(1, statuses.Count(s => s == HttpStatusCode.OK));
        var events = Events(capture);
        Assert.Single(events, e => e.EventId == SignInSucceeded);
        Assert.Single(events, e => e.EventId == OneTimeCodeFailed);
        AssertNoneOfTheSecretsAppear(capture, OwnerUsername, OwnerPassword, code, key);
    }

    [Fact]
    public async Task Window_CodesTwoStepsEitherSideAreAccepted_AndCodesThreeStepsAwayAreNot()
    {
        var capture = NewCapture();
        await WithOwnerHostAsync(capture, OwnerSecondFactor.EnabledWithoutCode, async (client, services) =>
        {
            var (_, key) = OwnerKey(services);
            await TotpTestCodes.WaitForSafeWindowAsync(8);

            foreach (var offset in RejectedOffsets)
            {
                var rejected = await SignInAsync(client, TotpTestCodes.Current(key, offset));
                Assert.True(rejected.Status == HttpStatusCode.OK, $"The code {offset} steps from the current one should be rejected.");
            }

            foreach (var offset in AcceptedOffsets)
            {
                var accepted = await SignInAsync(client, TotpTestCodes.Current(key, offset));
                Assert.True(accepted.Status == HttpStatusCode.Redirect, $"The code {offset} steps from the current one should be accepted once.");
            }

            return 0;
        });

        AssertCaptureIsNotVacuous(capture, "SignInSucceeded");
        var events = Events(capture);
        Assert.Equal(5, events.Count(e => e.EventId == SignInSucceeded));
        Assert.Equal(2, events.Count(e => e.EventId == OneTimeCodeFailed));
    }

    [Fact]
    public async Task ReplayCountsAsTheFifthFailure_AndLocksTheAccount_LikeAnyWrongCode()
    {
        var capture = NewCapture();
        var (key, code, state) = await WithOwnerHostAsync(capture, OwnerSecondFactor.EnabledWithoutCode, async (client, services) =>
        {
            var (_, ownerKey) = OwnerKey(services);
            var accepted = TotpTestCodes.NextUnused(ownerKey);
            var first = await SignInAsync(client, accepted);
            Assert.Equal(HttpStatusCode.Redirect, first.Status);

            // One two-factor session: four wrong codes, then the replay is the fifth failure (the password step resets the count).
            var (session, _) = NewClientLike(client);
            using (session)
            {
                var passwordStep = await PostPasswordAsync(session, OwnerUsername, OwnerPassword);
                var html = await passwordStep.Content.ReadAsStringAsync();
                var wrong = WrongCodeFor(ownerKey);
                for (var attempt = 1; attempt <= 4; attempt++)
                {
                    Assert.Equal(HttpStatusCode.OK, (await PostCodeAsync(session, html, wrong)).StatusCode);
                }

                var replay = await PostCodeAsync(session, html, accepted);
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                Assert.Contains("locked", await replay.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            }

            return (ownerKey, accepted, OwnerLockState(services));
        });

        AssertCaptureIsNotVacuous(capture, "AccountLockedOut");
        var events = Events(capture);
        Assert.Equal(1, events.Count(e => e.EventId == SignInSucceeded));
        Assert.Equal(5, events.Count(e => e.EventId == OneTimeCodeFailed));
        Assert.Equal(1, events.Count(e => e.EventId == AccountLockedOut));
        Assert.True(state.LockedOut, "The fifth consecutive failure locks the account for 15 minutes.");
        AssertNoneOfTheSecretsAppear(capture, OwnerUsername, OwnerPassword, code, key);
    }

    [Fact]
    public async Task Enrolment_TheActivationCodeCannotSignInStraightAway_AndTheNextCodeCan()
    {
        var capture = NewCapture();
        var (key, activationCode) = await WithOwnerHostAsync(capture, OwnerSecondFactor.NotActivated, async (client, services) =>
        {
            var (ownerId, ownerKey) = OwnerKey(services);
            var activationCode = TotpTestCodes.NextUnused(ownerKey);

            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
                var service = OwnerProvisioningService.Create(db);

                // A wrong code leaves the new account inactive.
                Assert.False(await service.VerifyTotpAndActivateAsync(ownerId, WrongCodeFor(ownerKey)));
                Assert.False(db.Users.AsNoTracking().Single(u => u.Id == ownerId).TwoFactorEnabled);

                Assert.True(await service.VerifyTotpAndActivateAsync(ownerId, activationCode));
                Assert.True(db.Users.AsNoTracking().Single(u => u.Id == ownerId).TwoFactorEnabled);
            }

            var replay = await SignInAsync(client, activationCode);
            Assert.Equal(HttpStatusCode.OK, replay.Status);

            var next = await SignInAsync(client, TotpTestCodes.NextUnused(ownerKey));
            Assert.Equal(HttpStatusCode.Redirect, next.Status);
            return (ownerKey, activationCode);
        });

        AssertCaptureIsNotVacuous(capture, "OneTimeCodeFailed");
        var events = Events(capture);
        Assert.Single(events, e => e.EventId == OneTimeCodeFailed);
        Assert.Single(events, e => e.EventId == SignInSucceeded);
        AssertNoneOfTheSecretsAppear(capture, OwnerUsername, OwnerPassword, activationCode, key);
    }

    [Fact]
    public async Task AnAlreadyUsedCode_CannotActivateAnAccount()
    {
        var capture = NewCapture();
        await WithOwnerHostAsync(capture, OwnerSecondFactor.NotActivated, async (_, services) =>
        {
            var (ownerId, ownerKey) = OwnerKey(services);
            var code = TotpTestCodes.NextUnused(ownerKey);

            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
            var service = OwnerProvisioningService.Create(db);
            Assert.True(await service.VerifyTotpAndActivateAsync(ownerId, code));

            // Switch the second factor off again and offer the same code: the provider would accept it, the guard must not.
            var user = db.Users.Single(u => u.Id == ownerId);
            user.TwoFactorEnabled = false;
            await db.SaveChangesAsync();

            Assert.False(await service.VerifyTotpAndActivateAsync(ownerId, code));
            Assert.False(db.Users.AsNoTracking().Single(u => u.Id == ownerId).TwoFactorEnabled);
            return 0;
        });
    }

    [Fact]
    public async Task FallbackBranch_ACodeWithNoTwoFactorSession_NeverSignsIn_AndWritesOneSignInFailed_WithNoSubject()
    {
        // Characterization (starts green by design): the page's fallback branch, taken when the two-factor user cannot be
        // resolved, fails closed today. If this ever signs the owner in, that branch bypasses the replay guard (ADR 0042).
        var capture = NewCapture();
        var (code, state, status, html, signedInCookie) = await WithOwnerHostAsync(capture, OwnerSecondFactor.EnabledWithoutCode, async (client, services) =>
        {
            var (_, key) = OwnerKey(services);
            var validCode = TotpTestCodes.NextUnused(key);

            var (session, cookies) = NewClientLike(client);
            using (session)
            {
                var token = await TokenAsync(session);
                var response = await session.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["ReturnUrl"] = "/",
                    ["RequiresTwoFactor"] = "true",
                    ["TwoFactorInput.Code"] = validCode,
                }));
                var body = await response.Content.ReadAsStringAsync();
                var hasAuthCookie = cookies.GetCookies(client.BaseAddress!).Cast<Cookie>()
                    .Any(c => c.Name.Contains("Identity.Application", StringComparison.OrdinalIgnoreCase));
                return (validCode, OwnerLockState(services), response.StatusCode, body, hasAuthCookie);
            }
        });

        AssertCaptureIsNotVacuous(capture, "SignInFailed");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(signedInCookie, "no authentication cookie may be issued without a two-factor session");
        Assert.Contains("Invalid verification code.", html, StringComparison.Ordinal);
        var events = Events(capture);
        var only = Assert.Single(events);
        Assert.Equal(SignInFailed, only.EventId);
        Assert.False(HasSubjectKey(only), "no account is named, so no subject property");
        Assert.Equal(0, state.AccessFailedCount);
        AssertNoneOfTheSecretsAppear(capture, OwnerUsername, OwnerPassword, code);
    }
}
