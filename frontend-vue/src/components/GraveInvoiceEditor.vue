<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Drawer from "primevue/drawer";
import Message from "primevue/message";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { createGraveInvoiceRevision, type InvoiceLineInput } from "@/api/endpoints";
import { useBlocking } from "@/composables/useBlocking";
import { useErrorToast } from "@/composables/useErrorToast";
import type { GraveInvoiceDraft } from "@/composables/useGraveInvoiceEdit";
import { type DraftLine, toLineInputs } from "@/lib/lines";
import LineEditor from "./LineEditor.vue";

const props = defineProps<{ draft: GraveInvoiceDraft | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();
const lines = ref<DraftLine[]>([]);

watch(visible, (open) => {
  if (open) {
    lines.value = props.draft?.lines ?? [];
  }
});

// No billing pre-check here, as in the UI5 app: the backend validates the revision.
async function save(): Promise<void> {
  const draft = props.draft;
  if (!draft) {
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
    await run(t("invoiceRunning"), () => createGraveInvoiceRevision(draft.invoiceId, inputs));
    await queryClient.invalidateQueries();
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
    :style="{ width: 'min(100vw, 60rem)' }"
  >
    <form id="graveInvoiceForm" class="flex flex-col gap-6" @submit.prevent="save">
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
