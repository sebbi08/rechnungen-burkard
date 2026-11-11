import { test } from "@playwright/test";
import { acquireStackLock } from "./lock";
import { GENERATION_LOCK_PATH } from "./stack";

// The backend runs one generation at a time and refuses a second one while the
// first is running (JobAlreadyRunning). The tests run in parallel
// workers, so the ones that generate take turns: through a lock file, the same
// kind of machine-wide lock the stack itself is guarded by (./lock.ts).
// Deleting and restoring a grave take the lock as well, because the server
// refuses both (JobAlreadyRunning) while a generation is running.

/** Runs `work` once no other test is generating, and keeps the others waiting until it is done. */
export async function withGenerationLock<T>(work: () => Promise<T>): Promise<T> {
  // a step of its own, so a test that times out waiting says what it waited
  // for, and on which file: one a dead process left can be removed by hand
  const step = `wait until no other test is generating (lock ${GENERATION_LOCK_PATH})`;
  const lock = await test.step(step, async () => {
    for (;;) {
      try {
        return acquireStackLock(GENERATION_LOCK_PATH, { waitMs: 0 });
      } catch (error) {
        // a file system error is not contention
        if ((error as NodeJS.ErrnoException).code) {
          throw error;
        }
        await new Promise((resolve) => setTimeout(resolve, 100));
      }
    }
  });
  try {
    return await work();
  } finally {
    lock.release();
  }
}
