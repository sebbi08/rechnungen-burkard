let issued = 0;

/**
 * A marker for the numbers and names a test seeds, unique across the whole run
 * and across earlier runs against the same stack.
 *
 * Every test runs on its own, in any worker, at the same moment as any other,
 * so a timestamp alone is not enough: two workers start a test in the same
 * millisecond often enough. The worker's index tells them apart, and the
 * counter tells apart two markers one worker takes in the same millisecond.
 * The timestamp keeps a rerun against a stack that kept its data from meeting
 * the rows of the run before.
 */
export function unique(): string {
  issued += 1;
  return `${Date.now()}-${process.env.TEST_WORKER_INDEX ?? "0"}-${issued}`;
}
