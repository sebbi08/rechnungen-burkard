import type { Ref } from "vue";
import { useI18n } from "vue-i18n";
import { useConfirmAsync } from "./useConfirmAsync";

/** Every way out of an editor drawer -- Cancel, its close button, a click
 *  beside it, Escape -- goes through `requestClose`, which asks before it drops
 *  anything typed since the editor was filled. Each of them used to close it
 *  without a word, and the grave editor is a long form.
 *
 *  `state` is what the user can change, as plain data; `markClean` takes it as
 *  the starting point once the editor is filled, new or loaded. */
export function useDiscardGuard(visible: Ref<boolean>, state: () => unknown) {
  const { t } = useI18n();
  const ask = useConfirmAsync();
  let clean: string | null = null;
  let asking = false;

  const snapshot = (): string => JSON.stringify(state());

  function markClean(): void {
    clean = snapshot();
  }

  async function requestClose(): Promise<void> {
    if (asking) {
      return;
    }
    if (clean !== null && snapshot() !== clean) {
      asking = true;
      try {
        if (!(await ask(t("confirmDiscardEditor")))) {
          return;
        }
      } finally {
        asking = false;
      }
    }
    visible.value = false;
  }

  /** For the drawer's @update:visible: an opening goes through, a closing asks. */
  function onVisibleChange(open: boolean): void {
    if (open) {
      visible.value = true;
    } else {
      void requestClose();
    }
  }

  return { markClean, requestClose, onVisibleChange };
}
