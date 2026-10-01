import { reactive } from "vue";

/** What BlockingProgress shows. One at a time: the UI is blocked while it runs. */
export const blockingState = reactive({
  active: false,
  title: "",
  percent: null as number | null,
});

/** Thrown when a long task is started while another is still running -- an
 *  Enter or Space on a focused Save button would otherwise submit it twice.
 *  useErrorToast ignores it: the running task speaks for itself. */
export class BlockingBusyError extends Error {
  public constructor() {
    super("Busy");
    this.name = "BlockingBusyError";
  }
}

/** Runs a long backend task behind a full-screen overlay (constitution VIII).
 *  The overlay closes in `finally`, so an error thrown by the task reaches the
 *  caller -- and its message -- only after the overlay is gone. A second call
 *  while one is running is refused. */
export function useBlocking() {
  async function run<T>(
    title: string,
    task: (progress: (percent: number, title?: string) => void) => Promise<T>,
  ): Promise<T> {
    if (blockingState.active) {
      throw new BlockingBusyError();
    }
    blockingState.active = true;
    blockingState.title = title;
    blockingState.percent = null;
    try {
      return await task((percent, nextTitle) => {
        blockingState.percent = percent;
        if (nextTitle) {
          blockingState.title = nextTitle;
        }
      });
    } finally {
      blockingState.active = false;
      blockingState.percent = null;
    }
  }
  return { run };
}
