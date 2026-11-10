using OtpNet;

namespace RoleRadar.Tests;

/// <summary>
/// Test-only source of one-time codes (feature 014, ADR 0042). The platform authenticator provider verifies codes but
/// cannot generate them, so tests ask Otp.NET. Not part of any shipped assembly. Linked into the Unit and Contract
/// projects, so there is one copy of this file.
/// <para>
/// A code that was accepted once is rejected as a replay for as long as it would otherwise be valid, so a test that
/// submits a code twice for one key inside one 30-second step must take its codes from <see cref="NextUnused"/> and
/// never reuse <see cref="Current"/> for the second submission.
/// </para>
/// </summary>
internal static class TotpTestCodes
{
    /// <summary>The offsets the platform provider accepts around the current step, in the order <see cref="NextUnused"/> tries them.</summary>
    private static readonly int[] OffsetOrder = [0, 1, -1, 2, -2];

    private const int StepSeconds = 30;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, HashSet<long>> UsedSteps = new(StringComparer.Ordinal);

    /// <summary>The code for the current 30-second step plus <paramref name="stepOffset"/> steps. Does not mark the code as used.</summary>
    public static string Current(string base32Key, int stepOffset = 0) =>
        At(base32Key, DateTime.UtcNow.AddSeconds(StepSeconds * stepOffset));

    /// <summary>The six-digit code (SHA-1, 30-second step) for a fixed UTC instant. Used for the RFC 6238 vectors.</summary>
    public static string At(string base32Key, DateTime utc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base32Key);
        var totp = new Totp(Base32Encoding.ToBytes(base32Key), step: StepSeconds, mode: OtpHashMode.Sha1, totpSize: 6);
        return totp.ComputeTotp(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
    }

    /// <summary>Whole seconds left in the current step (1 to 30).</summary>
    public static int SecondsRemainingInStep(DateTime? utcNow = null)
    {
        var seconds = new DateTimeOffset(DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc)).ToUnixTimeSeconds();
        return StepSeconds - (int)(seconds % StepSeconds);
    }

    /// <summary>Waits until at least <paramref name="minSecondsRemaining"/> seconds remain in the current step, so a window test never straddles a step boundary.</summary>
    public static async Task WaitForSafeWindowAsync(int minSecondsRemaining = 6)
    {
        var remaining = SecondsRemainingInStep();
        if (remaining < minSecondsRemaining)
        {
            await Task.Delay(TimeSpan.FromSeconds(remaining) + TimeSpan.FromMilliseconds(250));
        }
    }

    /// <summary>
    /// A code that has not been handed out before for this key. It tracks used step numbers per key and returns the code
    /// for the first unused step among the current step and the ones the provider accepts around it (offsets 0, +1, -1,
    /// +2, -2). When all five are used it waits for the next step boundary and tries again. <paramref name="now"/> and
    /// <paramref name="wait"/> are seams for tests of this helper; by default they are the real clock and
    /// <see cref="Thread.Sleep(TimeSpan)"/>.
    /// </summary>
    public static string NextUnused(string base32Key, Func<DateTime>? now = null, Action<TimeSpan>? wait = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base32Key);
        now ??= static () => DateTime.UtcNow;
        wait ??= static delay => Thread.Sleep(delay);

        while (true)
        {
            var instant = DateTime.SpecifyKind(now(), DateTimeKind.Utc);
            var current = new DateTimeOffset(instant).ToUnixTimeSeconds() / StepSeconds;

            lock (Gate)
            {
                if (!UsedSteps.TryGetValue(base32Key, out var used))
                {
                    used = [];
                    UsedSteps[base32Key] = used;
                }

                foreach (var offset in OffsetOrder)
                {
                    var step = current + offset;
                    if (used.Add(step))
                    {
                        return At(base32Key, DateTimeOffset.FromUnixTimeSeconds(step * StepSeconds).UtcDateTime);
                    }
                }
            }

            // All five steps in the window are used: wait for the next boundary (plus a margin) and look again.
            var remaining = SecondsRemainingInStep(instant);
            wait(TimeSpan.FromSeconds(remaining) + TimeSpan.FromMilliseconds(250));
        }
    }
}
