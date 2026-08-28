import { execFileSync } from "node:child_process";

/** The stack the suite runs against when E2E_BASE_URL is not set. */
export const OWN_STACK = !process.env.E2E_BASE_URL;

export const PORT = process.env.E2E_PORT ?? "8081";

export const BASE_URL = process.env.E2E_BASE_URL ?? `http://localhost:${PORT}`;

const COMPOSE_ARGS = [
  "compose",
  "-f",
  "../docker-compose.e2e.yml",
  "-p",
  "invoicing-e2e",
];

export function compose(...args: string[]): void {
  execFileSync("docker", [...COMPOSE_ARGS, ...args], {
    stdio: "inherit",
    // building both images from cold takes a while
    timeout: 15 * 60 * 1000,
  });
}

/** Polls until the frontend serves and its /odata proxy reaches the backend. */
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
