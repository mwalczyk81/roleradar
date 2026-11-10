import pLimit from "p-limit";
import { describe, expect, it } from "vitest";

// Feature 014 G3 (US3): the Insights loaders limit their in-flight requests with p-limit, not a hand-written limiter.
// These pin the properties the loaders rely on: at most `limit` tasks run together, first-come order, and a task that
// throws frees its slot and rejects only its own promise.

function deferred<T = void>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe("p-limit as the Insights loaders use it", () => {
  it("runs at most `limit` tasks together", async () => {
    const limit = pLimit(2);
    const gates = Array.from({ length: 6 }, () => deferred());
    let running = 0;
    let peak = 0;

    const results = gates.map((gate, index) =>
      limit(async () => {
        running += 1;
        peak = Math.max(peak, running);
        await gate.promise;
        running -= 1;
        return index;
      })
    );

    await Promise.resolve();
    expect(running).toBe(2);
    for (const gate of gates) {
      gate.resolve();
      await new Promise((resolve) => setTimeout(resolve, 0));
    }

    expect(await Promise.all(results)).toEqual([0, 1, 2, 3, 4, 5]);
    expect(peak).toBe(2);
  });

  it("starts waiting tasks in first-come order", async () => {
    const limit = pLimit(1);
    const started: number[] = [];
    const gates = Array.from({ length: 4 }, () => deferred());

    const results = gates.map((gate, index) =>
      limit(async () => {
        started.push(index);
        await gate.promise;
      })
    );

    for (const gate of gates) {
      gate.resolve();
      await new Promise((resolve) => setTimeout(resolve, 0));
    }
    await Promise.all(results);

    expect(started).toEqual([0, 1, 2, 3]);
  });

  it("a task that throws frees its slot and rejects only its own promise", async () => {
    const limit = pLimit(1);
    const failing = limit(() => Promise.reject(new Error("fictional failure")));
    const next = limit(() => Promise.resolve("ran"));

    await expect(failing).rejects.toThrow("fictional failure");
    await expect(next).resolves.toBe("ran");
    expect(limit.activeCount).toBe(0);
    expect(limit.pendingCount).toBe(0);
  });

  it("does not exceed the limit even momentarily when a task finishes and another is waiting", async () => {
    const limit = pLimit(1);
    let running = 0;
    let peak = 0;
    const task = async () => {
      running += 1;
      peak = Math.max(peak, running);
      await Promise.resolve();
      running -= 1;
    };

    await Promise.all([limit(task), limit(task), limit(task)]);

    expect(peak).toBe(1);
  });
});
