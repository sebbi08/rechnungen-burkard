import { compose, OWN_STACK, unlockStack } from "./stack";

// Runs even when tests fail, so an interrupted suite does not leave the port
// occupied for the next run. The lock is released last, once the stack is
// actually gone, so a run waiting on it does not start recreating the stack
// while this one is still tearing it down.
export default async function globalTeardown(): Promise<void> {
  if (!OWN_STACK) {
    return;
  }

  try {
    compose("down", "-v", "--remove-orphans");
  } finally {
    unlockStack();
  }
}
