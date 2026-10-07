import { execFileSync } from "node:child_process";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { acquireStackLock, type StackLock } from "./lock";

/** The stack the suite runs against when E2E_BASE_URL is not set. */
export const OWN_STACK = !process.env.E2E_BASE_URL;

export const PORT = process.env.E2E_PORT ?? "8081";

export const BASE_URL = process.env.E2E_BASE_URL ?? `http://localhost:${PORT}`;

const PROJECT = "invoicing-e2e";

const COMPOSE_ARGS = ["compose", "-f", "../docker-compose.e2e.yml", "-p", PROJECT];

// One lock per compose project, next to no other state: the thing being
// protected is the project and its port, both machine-wide.
const LOCK_PATH = join(tmpdir(), `${PROJECT}.stack.lock`);

/**
 * The lock graves.spec.ts's generating tests take turns through: the backend
 * runs one generation at a time. Machine-wide like the stack lock, since every
 * worker of a run has to see the same file.
 */
export const GENERATION_LOCK_PATH = join(tmpdir(), `${PROJECT}.generation.lock`);

// A queued run waits rather than failing -- a suite takes well under a minute,
// and an image build a few more -- but it does not wait forever, so a wedged
// run surfaces as an error instead of a hang.
const LOCK_WAIT_MS = 10 * 60 * 1000;

let held: StackLock | undefined;

/**
 * Claims the stack for this run. Without it a second E2E process would run
 * `down -v` in its own global-setup and destroy the stack, and the tmpfs
 * database with it, underneath a run already in progress -- which surfaces as
 * unrelated specs failing to connect rather than as an obvious collision.
 */
export function lockStack(): void {
  held = acquireStackLock(LOCK_PATH, {
    waitMs: LOCK_WAIT_MS,
    onWait: (message) => console.log(message),
  });
}

export function unlockStack(): void {
  held?.release();
  held = undefined;
}

export function compose(...args: string[]): void {
  execFileSync("docker", [...COMPOSE_ARGS, ...args], {
    stdio: "inherit",
    // building the image from cold takes a while
    timeout: 15 * 60 * 1000,
  });
}

/** Polls until the app serves and its OData endpoints answer. */
export async function waitUntilReady(timeoutMs = 5 * 60 * 1000): Promise<void> {
  const deadline = Date.now() + timeoutMs;
  let lastError = "not started";
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${BASE_URL}/odata/Settings`);
      if (response.ok) {
        return;
      }
      lastError = `HTTP ${response.status}`;
    } catch (error) {
      lastError = error instanceof Error ? error.message : String(error);
    }
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }

  throw new Error(`${BASE_URL} did not become ready: ${lastError}`);
}
