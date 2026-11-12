import { reactive, watch } from "vue";

/** A modal message that has to be read and dismissed: a refusal with an
 *  explanation, or a result summary. Transient errors are toasts instead. */
export const messageState = reactive<{
  visible: boolean;
  title: string;
  text: string;
  severity: "error" | "info";
}>({ visible: false, title: "", text: "", severity: "error" });

let dismissed: (() => void) | null = null;

// however the dialog closes -- OK, the close button, Escape -- the wait ends
watch(
  () => messageState.visible,
  (visible) => {
    if (!visible) {
      dismissed?.();
      dismissed = null;
    }
  },
);

export function showMessage(
  title: string,
  text: string,
  severity: "error" | "info" = "error",
): void {
  dismissed?.();
  dismissed = null;
  Object.assign(messageState, { visible: true, title, text, severity });
}

/** Shows the message and settles once it is dismissed, for a caller that has
 *  to wait for the owner to have read it. */
export function showMessageAndWait(
  title: string,
  text: string,
  severity: "error" | "info" = "error",
): Promise<void> {
  return new Promise((resolve) => {
    showMessage(title, text, severity);
    dismissed = resolve;
  });
}
