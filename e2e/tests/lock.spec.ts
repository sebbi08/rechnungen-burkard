import { expect, test } from "@playwright/test";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { acquireStackLock } from "../lock";

// The suite's stack is one machine-wide resource: a fixed compose project
// ("invoicing-e2e") on a fixed port. global-setup tears it down and recreates
// it, so a second E2E process would destroy the stack -- and its database --
// underneath a run already in progress. Every one of those failures looked like
// a flaky spec: the container is stopped with SIGTERM mid-run and every request
// after it fails to connect. These tests cover the lock that serialises runs.

function freshLockPath(): string {
  return join(mkdtempSync(join(tmpdir(), "lock-spec-")), "stack.lock");
}

test("a second acquisition fails while the first still holds the lock", async () => {
  const path = freshLockPath();
  const first = acquireStackLock(path, { waitMs: 0 });

  await expect(async () => {
    acquireStackLock(path, { waitMs: 0 });
  }).rejects.toThrow(/another E2E run/i);

  first.release();
});

test("the lock can be taken again once the holder releases it", async () => {
  const path = freshLockPath();
  acquireStackLock(path, { waitMs: 0 }).release();

  const second = acquireStackLock(path, { waitMs: 0 });
  expect(second).toBeTruthy();
  second.release();
});

test("a lock left behind by a dead process is taken over", async () => {
  const path = freshLockPath();
  // a pid that cannot be running: the kernel rejects it as out of range, which
  // is what a crashed run's leftover lock file looks like to the next run
  writeFileSync(path, "2147483647");

  const taken = acquireStackLock(path, { waitMs: 0 });
  expect(taken).toBeTruthy();
  taken.release();
});

test("releasing twice is harmless", async () => {
  const path = freshLockPath();
  const held = acquireStackLock(path, { waitMs: 0 });

  held.release();
  held.release();

  const again = acquireStackLock(path, { waitMs: 0 });
  again.release();
});

test("a lock file holding rubbish is not mistaken for a live holder", async () => {
  const path = freshLockPath();
  writeFileSync(path, "not-a-pid");

  const taken = acquireStackLock(path, { waitMs: 0 });
  expect(taken).toBeTruthy();
  taken.release();
});

test("waiting is announced once, so a queued run does not look hung", async () => {
  const path = freshLockPath();
  const first = acquireStackLock(path, { waitMs: 0 });
  const notices: string[] = [];

  await expect(async () => {
    acquireStackLock(path, {
      waitMs: 200,
      onWait: (message) => notices.push(message),
    });
  }).rejects.toThrow();

  expect(notices).toHaveLength(1);
  expect(notices[0]).toContain(path);

  first.release();
});

test("an uncontended lock announces nothing", async () => {
  const path = freshLockPath();
  const notices: string[] = [];

  const held = acquireStackLock(path, {
    waitMs: 200,
    onWait: (message) => notices.push(message),
  });

  expect(notices).toEqual([]);
  held.release();
});

test("the wait gives up with a message naming the lock file", async () => {
  const path = freshLockPath();
  const first = acquireStackLock(path, { waitMs: 0 });

  const started = Date.now();
  await expect(async () => {
    acquireStackLock(path, { waitMs: 300 });
  }).rejects.toThrow(path);
  expect(Date.now() - started).toBeGreaterThanOrEqual(250);

  first.release();
  rmSync(path, { force: true });
});
