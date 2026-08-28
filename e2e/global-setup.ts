import { compose, OWN_STACK, waitUntilReady } from "./stack";

// Brings up the suite's own stack: separate project, separate containers, and a
// tmpfs database, so a run never sees or touches the dev stack's data.
// --force-recreate guarantees that database starts out empty.
export default async function globalSetup(): Promise<void> {
  if (!OWN_STACK) {
    return;
  }

  compose("down", "-v", "--remove-orphans");
  compose("up", "-d", "--build", "--force-recreate");
  await waitUntilReady();
}
