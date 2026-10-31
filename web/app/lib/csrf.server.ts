import crypto from "node:crypto";

export function generateCsrfToken(): string {
  return crypto.randomBytes(32).toString("hex");
}

export function validateCsrfToken(
  submitted: string | null | undefined,
  expected: string | null | undefined
): boolean {
  if (!submitted || !expected) {
    return false;
  }

  const submittedBuf = Buffer.from(submitted, "utf8");
  const expectedBuf = Buffer.from(expected, "utf8");

  if (submittedBuf.length !== expectedBuf.length) {
    return false;
  }

  return crypto.timingSafeEqual(submittedBuf, expectedBuf);
}
