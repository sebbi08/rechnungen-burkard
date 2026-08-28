import { compose, OWN_STACK } from "./stack";

// Runs even when tests fail, so an interrupted suite does not leave the port
// occupied for the next run.
export default async function globalTeardown(): Promise<void> {
  if (!OWN_STACK) {
    return;
  }

  compose("down", "-v", "--remove-orphans");
}
