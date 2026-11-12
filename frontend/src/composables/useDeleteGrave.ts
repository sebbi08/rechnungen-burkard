import { useQueryClient } from "@tanstack/vue-query";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import { deleteGrave } from "@/api/endpoints";
import { useConfirmAsync } from "@/composables/useConfirmAsync";
import { useErrorToast } from "@/composables/useErrorToast";

/** Deleting a grave, the same from its page and from a list: always asked
 *  first with the consequences spelled out, and every query refreshed
 *  afterwards -- also when the server refused, since the page that offered the
 *  action may have been out of date. */
export function useDeleteGrave() {
  const { t } = useI18n();
  const ask = useConfirmAsync();
  const { showError } = useErrorToast();
  const queryClient = useQueryClient();
  const deleting = ref(false);

  /** Asks, then deletes; answers whether the grave was deleted. */
  async function removeGrave(graveId: number): Promise<boolean> {
    if (!(await ask(t("confirmDeleteGrave")))) {
      return false;
    }
    deleting.value = true;
    try {
      await deleteGrave(graveId);
      return true;
    } catch (error) {
      showError(error);
      return false;
    } finally {
      await queryClient.invalidateQueries();
      deleting.value = false;
    }
  }

  return { removeGrave, deleting };
}
