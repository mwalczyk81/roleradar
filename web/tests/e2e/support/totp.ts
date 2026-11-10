import { Secret, TOTP } from "otpauth";

/**
 * One-time codes for the end-to-end specs (feature 014, ADR 0042). The identity host verifies codes with the ASP.NET Core
 * authenticator provider and cannot generate them, so the specs ask the maintained `otpauth` library (RFC 6238: SHA-1,
 * 30 second step, six digits). Test-only: nothing here ships.
 *
 * The identity host also rejects a code it has already accepted, so a sign-in must never reuse one. Use
 * nextUnusedTotpCode for every sign-in; totpCode with an explicit time stays a pure function for the RFC 6238 vectors.
 */
const STEP_MS = 30_000;

/** The offsets the provider accepts around the current step, in the order nextUnusedTotpCode tries them. */
const OFFSET_ORDER = [0, 1, -1, 2, -2];

function normalise(secret: string): string {
  return secret.replace(/[\s=]/g, "").toUpperCase();
}

/** Base32 to bytes, ignoring case, spaces and padding. */
export function base32Decode(input: string): Uint8Array {
  return new Uint8Array(Secret.fromBase32(normalise(input)).bytes);
}

function generator(base32Secret: string): TOTP {
  return new TOTP({ algorithm: "SHA1", digits: 6, period: 30, secret: Secret.fromBase32(normalise(base32Secret)) });
}

/** The six-digit code for `nowMs` (default: now). A pure function of the secret and the time. */
export function totpCode(base32Secret: string, nowMs: number = Date.now()): string {
  if (!base32Secret || base32Secret.trim() === "") {
    throw new Error("A TOTP secret is required.");
  }
  return generator(base32Secret).generate({ timestamp: nowMs });
}

const usedSteps = new Map<string, Set<number>>();

/**
 * A code that was not handed out before for this secret. It tracks used step numbers per secret and returns the code for the
 * first unused step among the current step and the ones the provider accepts around it (offsets 0, +1, -1, +2, -2). When all
 * five are used it waits for the next step boundary and tries again. `now` is a seam for tests of this helper.
 */
export async function nextUnusedTotpCode(base32Secret: string, now: () => number = Date.now): Promise<string> {
  if (!base32Secret || base32Secret.trim() === "") {
    throw new Error("A TOTP secret is required.");
  }

  const key = normalise(base32Secret);
  let used = usedSteps.get(key);
  if (!used) {
    used = new Set<number>();
    usedSteps.set(key, used);
  }

  for (;;) {
    const current = Math.floor(now() / STEP_MS);
    for (const offset of OFFSET_ORDER) {
      const step = current + offset;
      if (!used.has(step)) {
        used.add(step);
        return totpCode(base32Secret, step * STEP_MS);
      }
    }

    // Every step in the window is used: wait for the next boundary (plus a margin) and look again.
    const untilNextStep = STEP_MS - (now() % STEP_MS) + 250;
    await new Promise<void>((resolve) => setTimeout(resolve, untilNextStep));
  }
}
