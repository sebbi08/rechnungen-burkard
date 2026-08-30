import { closeSync, openSync, readFileSync, rmSync, writeSync } from "node:fs";

/**
 * A machine-wide lock around the suite's stack.
 *
 * The stack is a single shared resource: one compose project ("invoicing-e2e")
 * on one port. global-setup tears it down and recreates it, so a second E2E
 * process on the same machine destroys the stack -- and its tmpfs database --
 * underneath a run that is already going. What that looks like from the test
 * side is not an obvious infrastructure error but a scatter of unrelated
 * failures: the container is stopped with SIGTERM mid-run, every request after
 * it fails to connect, and whichever spec touches the most shared state gets
 * the blame. So runs are serialised rather than left to race.
 *
 * The lock is a file holding the owner's pid, created with the exclusive flag,
 * which is atomic. A run that dies without releasing leaves the file behind, so
 * a lock whose pid is no longer running is treated as stale and taken over --
 * otherwise one crashed run would block the machine until someone deleted it
 * by hand.
 */

export interface StackLock {
  release(): void;
}

export interface AcquireOptions {
  /** How long to wait for another run to finish. 0 fails immediately. */
  waitMs?: number;
  /**
   * Called once, only when the lock is actually contended. A run that queues
   * behind another otherwise sits silent for minutes and reads as a hang.
   */
  onWait?: (message: string) => void;
}

const POLL_MS = 50;

/** Whether a pid belongs to a process that still exists. */
function isAlive(pid: number): boolean {
  try {
    // signal 0 performs the permission and existence checks without signalling
    process.kill(pid, 0);
    return true;
  } catch (error) {
    // EPERM means the process exists but belongs to someone else
    return (error as NodeJS.ErrnoException).code === "EPERM";
  }
}

/** Removes a lock file whose owner is gone. Answers whether it did. */
function clearIfStale(path: string): boolean {
  let owner: number;
  try {
    owner = Number.parseInt(readFileSync(path, "utf8").trim(), 10);
  } catch {
    // it vanished between the failed create and this read: treat as cleared
    return true;
  }

  if (Number.isFinite(owner) && owner > 0 && isAlive(owner)) {
    return false;
  }

  rmSync(path, { force: true });
  return true;
}

/** Sleeps without yielding to the event loop, so this stays synchronous. */
function sleep(ms: number): void {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms);
}

function tryCreate(path: string): boolean {
  let fd: number;
  try {
    fd = openSync(path, "wx");
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code === "EEXIST") {
      return false;
    }
    throw error;
  }

  try {
    writeSync(fd, String(process.pid));
  } finally {
    closeSync(fd);
  }
  return true;
}

/**
 * Takes the lock, waiting up to `waitMs` for another run to release it.
 * Throws when the wait runs out, naming the file so it can be removed by hand
 * if a run really did leave it behind.
 */
export function acquireStackLock(path: string, options: AcquireOptions = {}): StackLock {
  const waitMs = options.waitMs ?? 0;
  const deadline = Date.now() + waitMs;
  let announced = false;

  for (;;) {
    if (tryCreate(path)) {
      return makeLock(path);
    }

    // a lock whose owner died is not a lock; take it over
    if (clearIfStale(path) && tryCreate(path)) {
      return makeLock(path);
    }

    if (!announced) {
      announced = true;
      options.onWait?.(`waiting for another E2E run to release the stack lock ${path}`);
    }

    if (Date.now() >= deadline) {
      throw new Error(
        `another E2E run holds the stack lock ${path}. The suite's stack is a ` +
          `single compose project on one port, so runs cannot overlap: a second ` +
          `run would tear the stack down under the first. Wait for it to finish, ` +
          `or delete that file if no run is left.`,
      );
    }

    sleep(Math.min(POLL_MS, Math.max(0, deadline - Date.now())));
  }
}

function makeLock(path: string): StackLock {
  let held = true;

  const release = (): void => {
    if (!held) {
      return;
    }
    held = false;
    process.off("exit", release);
    rmSync(path, { force: true });
  };

  // a run killed mid-flight should not leave the machine locked
  process.on("exit", release);
  return { release };
}
