import { expect, afterEach } from "vitest";
import { cleanup } from "@testing-library/react";
import axe from "axe-core";
import nodeCrypto from "node:crypto";

if (nodeCrypto.webcrypto) {
  Object.defineProperty(globalThis, "crypto", {
    value: nodeCrypto.webcrypto,
    configurable: true,
  });

  const originalDigest = nodeCrypto.webcrypto.subtle.digest.bind(
    nodeCrypto.webcrypto.subtle
  );
  nodeCrypto.webcrypto.subtle.digest = (async (
    algorithm: AlgorithmIdentifier,
    data: BufferSource
  ) => {
    const result = await originalDigest(algorithm, data);
    if (!(result instanceof ArrayBuffer)) {
      const buf = result as unknown as ArrayBuffer;
      const copy = new ArrayBuffer(buf.byteLength);
      new Uint8Array(copy).set(new Uint8Array(buf));
      return copy;
    }
    return result;
  }) as typeof nodeCrypto.webcrypto.subtle.digest;
}

if (typeof HTMLCanvasElement !== "undefined") {
  HTMLCanvasElement.prototype.getContext = () => null;
}

// By default in test harness, require secure cookies to verify production-grade security invariants
process.env.COOKIE_SECURE = "true";

afterEach(() => {
  cleanup();
});

declare module "vitest" {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface Assertion<R extends void | Promise<void> = void, T = unknown> {
    toHaveNoViolations(): Promise<R>;
  }
  interface AsymmetricMatchersContaining {
    toHaveNoViolations(): void;
  }
}

expect.extend({
  async toHaveNoViolations(received: Element | string) {
    const results = await axe.run(received as any, {
      resultTypes: ["violations"],
    });

    const pass = results.violations.length === 0;
    if (pass) {
      return {
        pass: true,
        message: () => "Expected accessibility violations, but found none.",
      };
    }

    const formattedViolations = results.violations
      .map(
        (v) =>
          `[${v.impact?.toUpperCase()}] ${v.id}: ${v.help}\n  Help URL: ${v.helpUrl}\n  Nodes:\n` +
          v.nodes.map((n) => `    - ${n.html}\n      ${n.failureSummary}`).join("\n")
      )
      .join("\n\n");

    return {
      pass: false,
      message: () =>
        `Expected zero accessibility violations, but found ${results.violations.length}:\n\n${formattedViolations}`,
    };
  },
});
