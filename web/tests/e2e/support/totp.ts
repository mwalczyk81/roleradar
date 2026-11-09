import crypto from "node:crypto";

/**
 * RFC 6238 TOTP (SHA-1, 30 second step, six digits), the same algorithm the identity host uses. Used by the
 * screenshot spec to answer the sign-in challenge from the demo account's authenticator secret.
 */
const ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

export function base32Decode(input: string): Uint8Array {
  const clean = input.replace(/[\s=]/g, "").toUpperCase();
  const bytes: number[] = [];
  let buffer = 0;
  let bits = 0;
  for (const char of clean) {
    const value = ALPHABET.indexOf(char);
    if (value < 0) {
      continue;
    }
    buffer = (buffer << 5) | value;
    bits += 5;
    if (bits >= 8) {
      bytes.push((buffer >> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return Uint8Array.from(bytes);
}

export function totpCode(base32Secret: string, nowMs: number = Date.now()): string {
  if (!base32Secret || base32Secret.trim() === "") {
    throw new Error("A TOTP secret is required.");
  }
  const key = base32Decode(base32Secret);
  const step = Math.floor(nowMs / 1000 / 30);
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const digest = crypto.createHmac("sha1", key).update(counter).digest();
  const offset = digest[digest.length - 1] & 0x0f;
  const binary = digest.readUInt32BE(offset) & 0x7fffffff;
  return String(binary % 1_000_000).padStart(6, "0");
}
