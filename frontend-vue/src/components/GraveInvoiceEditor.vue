<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Drawer from "primevue/drawer";
import Message from "primevue/message";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import {
  checkGraveBilling,
  createGraveInvoiceRevision,
  type InvoiceLineInput,
} from "@/api/endpoints";
import { useBlocking } from "@/composables/useBlocking";
import { useDrawerFocus } from "@/composables/useDrawerFocus";
import { useErrorToast } from "@/composables/useErrorToast";
import type { GraveInvoiceDraft } from "@/composables/useGraveInvoiceEdit";
import { showMessage } from "@/composables/useMessage";
import { billingProblemText } from "@/lib/billing";
import type { Translate } from "@/lib/format";
import { type DraftLine, toLineInputs } from "@/lib/lines";
import LineEditor from "./LineEditor.vue";

const props = defineProps<{ draft: GraveInvoiceDraft | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const translate: Translate = (key, list) => (list ? t(key, list) : t(key));
const { showError } = useErrorToast();
const drawerFocus = useDrawerFocus();
const formEl = ref<HTMLFormElement | null>(null);
const { run } = useBlocking();
const queryClient = useQueryClient();
const lines = ref<DraftLine[]>([]);

watch(visible, (open) => {
  if (open) {
    drawerFocus.remember();
    lines.value = props.draft?.lines ?? [];
  }
});

async function save(): Promise<void> {
  const draft = props.draft;
  // a click landing while the drawer slides out after a save
  if (!draft || !visible.value) {
    return;
  }
  let inputs: InvoiceLineInput[];
  try {
    inputs = toLineInputs(lines.value);
  } catch (error) {
    showError(error);
    return;
  }
  try {
    // The backend refuses prices that do not split across the recipients, but
    // only by name; the check first lets the message say which rate and what
    // would work, as the grave editor does. Under the overlay with the save and
    // the refetch, so a second Save cannot slip in (InvoiceEditor).
    const problems = await run(t("invoiceRunning"), async () => {
      // a check that cannot be reached does not block: the save validates again
      const check = await checkGraveBilling(draft.recipients, inputs).catch(() => null);
      if (check && !check.Ok) {
        return billingProblemText(check.Problems, draft.recipients, translate);
      }
      await createGraveInvoiceRevision(draft.invoiceId, inputs);
      await queryClient.invalidateQueries();
      return null;
    });
    if (problems) {
      showMessage(t("pricesDoNotDivideTitle"), problems);
      return;
    }
    visible.value = false;
  } catch (error) {
    showError(error);
  }
}
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    :header="t('graveInvoiceEditTitle')"
    :aria-label="t('graveInvoiceEditTitle')"
    :style="{ width: 'min(100vw, 60rem)' }"
    @after-show="drawerFocus.focusFirst(formEl)"
    @after-hide="drawerFocus.restore()"
    @keydown.capture="drawerFocus.onKeydown($event, () => (visible = false))"
  >
    <form ref="formEl" id="graveInvoiceForm" class="flex flex-col gap-6" @submit.prevent="save">
      <Message v-if="draft" severity="warn">{{ draft.warning }}</Message>
      <LineEditor v-model="lines" />
    </form>
    <template #footer>
      <div class="flex justify-end gap-2">
        <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
        <Button type="submit" form="graveInvoiceForm" :label="t('save')" icon="pi pi-check" />
      </div>
    </template>
  </Drawer>
</template>
