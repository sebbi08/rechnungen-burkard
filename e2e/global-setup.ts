import { compose, lockStack, OWN_STACK, waitUntilReady } from "./stack";

// Brings up the suite's own stack: separate project, separate containers, and a
// tmpfs database, so a run never sees or touches the dev stack's data.
// --force-recreate guarantees that database starts out empty.
//
// The lock comes first, and is held until global-teardown: the stack is one
// compose project on one port, so a second run's `down -v` would take it out
// from under this one.
export default async function globalSetup(): Promise<void> {
  if (!OWN_STACK) {
    return;
  }

  lockStack();
  compose("down", "-v", "--remove-orphans");
  compose("up", "-d", "--build", "--force-recreate");
  await waitUntilReady();
}
