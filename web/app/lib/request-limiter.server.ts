/**
 * A small concurrency limiter shared by the Insights requests (the counts and the stats read), so that at most `limit`
 * requests are in flight together. `run` starts the task at once when a slot is free, otherwise it waits its turn in
 * first-come order and inherits the slot of the task that finished (so the limit is never exceeded, not even
 * momentarily). A task that throws frees its slot and rejects only its own promise.
 */
export interface Limiter {
  run<T>(task: () => Promise<T>): Promise<T>;
}

export function createLimiter(limit: number): Limiter {
  let active = 0;
  const waiting: Array<() => void> = [];

  return {
    async run<T>(task: () => Promise<T>): Promise<T> {
      if (active >= limit) {
        // The finishing task hands its slot straight to this one, so `active` stays unchanged.
        await new Promise<void>((resolve) => waiting.push(resolve));
      } else {
        active++;
      }

      try {
        return await task();
      } finally {
        const next = waiting.shift();
        if (next) {
          next();
        } else {
          active--;
        }
      }
    },
  };
}
