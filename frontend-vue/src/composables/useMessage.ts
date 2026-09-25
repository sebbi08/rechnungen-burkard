import { reactive } from "vue";

/** A modal message that has to be read and dismissed: a refusal with an
 *  explanation, or a result summary. Transient errors are toasts instead. */
export const messageState = reactive<{
  visible: boolean;
  title: string;
  text: string;
  severity: "error" | "info";
}>({ visible: false, title: "", text: "", severity: "error" });

export function showMessage(
  title: string,
  text: string,
  severity: "error" | "info" = "error",
): void {
  Object.assign(messageState, { visible: true, title, text, severity });
}
