# ADR 0042: One-Time Codes Through the Platform Authenticator Provider, With a Temporary Replay Guard

- **Status**: Accepted (2026-11-10)
- **Date**: 2026-11-10
- **Feature**: `014-standards-first` (story P1, Jira RR-77)

## Context and Problem Statement

The identity service checked the owner's one-time code with hand-written RFC 6238 and Base32 code (`OwnerProvisioningService`), although ASP.NET Core Identity ships an authenticator token provider that does the same job and that ADR 0017 already named. The sign-in page used the hand-written check for every normal sign-in and reached the platform provider only through a fallback branch. The enrolment activation (`create-owner`) and the demo `totp` command used the hand-written code too.

Principle X (Standards First) asks for the standard unless a recorded reason says it does not fit. Two facts decide the details, both measured on 2026-11-10 against Identity 10.0.14 in a scratch project and now pinned by tests (`AuthenticatorProviderBehaviorTests`):

- The platform provider accepts the code for the current 30-second step and two steps either side (five codes, up to about 150 seconds). The hand-written check accepted one step either side (three codes).
- The provider accepts the same code any number of times. RFC 6238 section 5.2 says the verifier MUST NOT accept the second attempt of an OTP after a successful validation. The hand-written check had no used-code record either, so the gap existed before this feature.
- The provider verifies codes but cannot generate them: asked for a token it returns an empty string.

## Decision Outcome

- **Verify with the platform provider.** `OwnerProvisioningService.VerifyTotpAndActivateAsync` (sign-in and enrolment activation) calls `UserManager.VerifyTwoFactorTokenAsync` with `TokenOptions.DefaultAuthenticatorProvider`. Keys already stored by the hand-written code (Base32, in the Identity authenticator-key slot) are read back unchanged: no re-enrolment (test `KeyInTodaysFormat_IsReadBackUnchanged_AndItsCurrentCodeIsAccepted`). New keys come from `UserManager.GenerateNewAuthenticatorKey()`. Lockout stays at 5 attempts and 15 minutes, and the sign-in events 5001 to 5004 fire as before.
- **The window is the provider's, which is wider than RFC 6238 recommends.** RFC 6238 section 5.2 recommends accepting at most one time step of drift. The provider accepts two either side and cannot be configured (`dotnet/aspnetcore#27088`, open since 2020). Narrowing it would need a hand-written code check again, which is the thing this feature removes. This is a deliberate deviation, accepted with two mitigations: the replay guard below (a code works once), and the lockout (5 failed attempts, 15 minutes). No in-repo code narrows the window.
- **The replay guard is a thin, temporary in-repo extension** (`AuthenticatorCodeGuard`). It is called only after the provider accepts a code. It records an HMAC-SHA-256 of the code, keyed by the user's authenticator key, in a new Identity table (`UsedAuthenticatorCodes`); the primary key (user id, code hash) makes two simultaneous attempts safe, because exactly one insert wins. Rows are kept for 5 minutes (a code is valid for at most 150 seconds after acceptance) and removed by the next insert. The record holds no code and survives a restart. A replay is treated as a wrong code: `AccessFailedAsync`, event 5004, the lockout rules unchanged.
  - **Known limit.** The provider does not say which step matched, so the record is per code value. A later genuine code that equals a used one inside the 5-minute retention (about one chance in 100,000) is rejected once and the owner enters the next code.
  - **Visible consequence.** The owner who enrols with a code and signs in a few seconds later with the same code is rejected as a replay and waits for the next code (at most 30 seconds).
  - **When to delete it.** When upstream rejects reuse itself. `RawProvider_AcceptsTheSameCodeRepeatedly` pins the gap and fails then; delete the guard, the table (a new migration) and this paragraph.
  - **Upstream evidence** (read-only search of `dotnet/aspnetcore`, 2026-11-10): `#14423` "User can verify multiple times with the same two factor token" was closed as completed on 2024-08-16, the day after `dotnet/AspNetCore.Docs#33373` ("Document that TOTP codes can be reused before it expires") was merged, so upstream documented the behavior rather than changing it; a community pull request, `#57728`, was closed without merging on 2024-10-11. `#54524` (the authenticator does not comply with RFC 6238) is open. `#45969` was closed as a duplicate of `#14423`. **Nothing was filed, commented on or reacted to. An upstream contribution awaits the owner's explicit approval** (Principle X), and `dotnet/aspnetcore` requires the .NET Foundation CLA.
- **The sign-in page no longer has an unguarded verification path.** Its fallback branch, taken when the two-factor user cannot be resolved, called `SignInManager.TwoFactorAuthenticatorSignInAsync`, which goes straight to the provider and would accept a replayed code. The branch fails closed today (that call needs the same two-factor cookie): a test posts a valid code with no two-factor session and sees no sign-in, no authentication cookie and exactly one `SignInFailed` (5002) without a subject. The branch now says so explicitly and writes the same event, and a source scan fails if the page calls `TwoFactorAuthenticatorSignInAsync` again.
- **Code source for the demo command and the tests.** The demo `totp` command ships in the CLI, so it may not hold a hand-written generator: it uses **Otp.NET 1.4.1** (MIT, the licence file in the package was read), which also builds the enrolment URI (`OtpUri`) and the demo key. The web end-to-end helper uses **`otpauth` 9.5.2** (MIT, dev dependency), replacing two hand-written copies (`web/tests/e2e/support/totp.ts` and a private copy in `real-stack.spec.ts`). A test-only helper was compared and rejected: the libraries cost a package and no runtime code, and both sides keep the RFC 6238 Appendix B vectors as the parity proof (`OtpNet_MatchesRfc6238AppendixBVectors`, `totp-helper.test.ts`).
  - **One visible difference in the enrolment URI.** Otp.NET writes the default parameters out: `...&issuer=RoleRadar&algorithm=SHA1&digits=6&period=30`. The Key URI Format allows them, they equal the defaults, and they match what the platform provider validates (SHA-1, 6 digits, 30 seconds). Existing enrolments are unaffected, since the URI is shown only at enrolment. An agent could not test real authenticator apps, so the owner should scan one newly generated enrolment QR code once.
- **Principle IX.** The new table is ours and carries `OwnerId`, always equal to the user id. Framework-owned ASP.NET Identity tables (`AspNetUsers`, `AspNetUserTokens` and the rest) are exempt from the `owner_id` rule because we do not create them and cannot add columns without forking the schema.

## Alternatives Considered

- **Keep the hand-written verifier with an ADR.** It has no replay protection either, and nothing makes it better than the platform's.
- **Otp.NET as the verifier** (`VerifyTotp`, which also returns the matched step). It would replace the provider in the sign-in path, where the lockout and security-stamp handling already hang off it, and it would still need our own store.
- **Track the last accepted time step instead of the code.** Needs the matched step, which the provider does not return, so it would need a second hand-written verifier.
- **An in-memory record.** Lost on a restart inside the window, so a code could be replayed after one.
- **Narrow the window in-repo.** Needs a hand-written code check (see above).

## Consequences

- **Good**: the hand-written RFC 6238 and Base32 code is gone from shipped source (a source scan enforces it); a code can be used once; existing owners keep their authenticator app and secret; one fewer place where a security mechanism is our own.
- **Bad**: the accepted window is wider than before (about 150 seconds of validity, mitigated by single use and lockout); one small temporary extension and one table to maintain until upstream changes; a rare, harmless double-entry when two genuine codes collide.
- **Packages**: Otp.NET 1.4.1 (NuGet, referenced by `RoleRadar.Identity`, reaching the CLI and test projects through project references; their lock files were regenerated) and `otpauth` 9.5.2 (npm dev dependency, which brings `@noble/hashes` 2.4.0 and moved jsdom's optional peer from 1.8.0 to 2.4.0 in `pnpm-lock.yaml`).
