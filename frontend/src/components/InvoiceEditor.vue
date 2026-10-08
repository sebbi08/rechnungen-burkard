<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Drawer from "primevue/drawer";
import ProgressSpinner from "primevue/progressspinner";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import {
  createInvoice,
  createRevision,
  fetchInvoiceLines,
  type InvoiceLineInput,
} from "@/api/endpoints";
import { ApiError } from "@/api/http";
import { loadItemVatRates } from "@/api/queries";
import { useBlocking } from "@/composables/useBlocking";
import { useDiscardGuard } from "@/composables/useDiscardGuard";
import { useDrawerFocus } from "@/composables/useDrawerFocus";
import { useErrorToast } from "@/composables/useErrorToast";
import { type DraftLine, newLine, toLineInputs } from "@/lib/lines";
import { priceFieldValue } from "@/lib/money";
import LineEditor from "./LineEditor.vue";
import RecipientPicker from "./RecipientPicker.vue";

const props = defineProps<{ target: { invoiceId: number; recipientId: number } | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const { showError } = useErrorToast();
const drawerFocus = useDrawerFocus();
const formEl = ref<HTMLFormElement | null>(null);
const { run } = useBlocking();
const queryClient = useQueryClient();

const recipientId = ref<number | null>(null);
const lines = ref<DraftLine[]>([]);
const loading = ref(false);
// What the user can change. A new invoice's recipient is left out: the picker
// fills it in by itself, after the editor has opened.
const discard = useDiscardGuard(visible, () => ({
  recipient: props.target ? recipientId.value : null,
  lines: lines.value.map(({ itemId, count, unitPrice, priceIncludesVat }) => ({
    itemId,
    count,
    unitPrice,
    priceIncludesVat,
  })),
}));
// Which opening a load belongs to: lines that arrive after the drawer was
// closed, or reopened for another invoice or a new one, are dropped (GraveEditor).
let opening = 0;

watch(visible, async (open) => {
  const current = ++opening;
  if (open) {
    drawerFocus.remember();
  }
  if (!open) {
    loading.value = false;
    return;
  }
  recipientId.value = props.target?.recipientId ?? null;
  lines.value = [newLine()];
  if (!props.target) {
    discard.markClean();
    return;
  }
  loading.value = true;
  try {
    const [existing, rates] = await Promise.all([
      fetchInvoiceLines(props.target.invoiceId),
      loadItemVatRates(queryClient),
    ]);
    if (current !== opening) {
      return;
    }
    lines.value = existing
      .slice()
      .sort((a, b) => a.LineNumber - b.LineNumber)
      .map((line) =>
        newLine({
          itemId: line.ItemId,
          count: line.Count,
          // the line stores a net; one entered VAT-included is edited as its
          // gross, converted with the item's current rate (what the backend uses)
          unitPrice: priceFieldValue(
            line.UnitPrice,
            line.PriceIncludesVat,
            (line.ItemId != null ? rates.get(line.ItemId) : undefined) ?? line.VatRate,
          ),
          priceIncludesVat: line.PriceIncludesVat,
        }),
      );
    discard.markClean();
  } catch (error) {
    if (current === opening) {
      showError(error);
      visible.value = false;
    }
  } finally {
    if (current === opening) {
      loading.value = false;
      void drawerFocus.focusFirst(formEl.value);
    }
  }
});

async function save(): Promise<void> {
  // a click landing while the drawer slides out after a save
  if (!visible.value) {
    return;
  }
  let inputs: InvoiceLineInput[];
  try {
    inputs = toLineInputs(lines.value);
  } catch (error) {
    showError(error);
    return;
  }
  const target = props.target;
  // with no recipient chosen the backend answered "not found", which blamed
  // a record instead of the missing choice
  if (recipientId.value == null) {
    showError(new ApiError("RecipientRequired"));
    return;
  }
  const recipient = recipientId.value;
  try {
    // The refetch runs under the overlay too: a drawer left open while the lists
    // reload, with the busy guard already released, takes a second Save and
    // creates the invoice twice.
    await run(t("invoiceRunning"), async () => {
      await (target
        ? createRevision(target.invoiceId, recipient, inputs)
        : createInvoice(recipient, inputs));
      await queryClient.invalidateQueries();
    });
    visible.value = false;
  } catch (error) {
    showError(error);
  }
}
</script>

<template>
  <Drawer
    :visible="visible"
    :close-on-escape="false"
    position="right"
    :header="target ? t('editInvoice') : t('createInvoice')"
    :aria-label="target ? t('editInvoice') : t('createInvoice')"
    :style="{ width: 'min(100vw, 60rem)' }"
    @after-show="drawerFocus.focusFirst(formEl)"
    @after-hide="drawerFocus.restore()"
    @update:visible="discard.onVisibleChange"
    @keydown.capture="drawerFocus.onKeydown($event, discard.requestClose)"
  >
    <div v-if="loading" class="flex justify-center p-10"><ProgressSpinner /></div>
    <form ref="formEl" v-else id="invoiceForm" class="flex flex-col gap-6" @submit.prevent="save">
      <div class="flex flex-col gap-2">
        <label for="invoiceRecipient" class="font-medium">{{ t("menuRecipients") }}</label>
        <RecipientPicker
          v-model="recipientId"
          input-id="invoiceRecipient"
          :default-to-newest="!target"
        />
      </div>
      <div class="flex flex-col gap-2">
        <span class="font-medium">{{ t("menuItems") }}</span>
        <LineEditor v-model="lines" />
      </div>
    </form>
    <template #footer>
      <div class="flex justify-end gap-2">
        <Button :label="t('cancel')" severity="secondary" text @click="discard.requestClose" />
        <Button
          type="submit"
          form="invoiceForm"
          :label="t('save')"
          icon="pi pi-check"
          :disabled="loading"
        />
      </div>
    </template>
  </Drawer>
</template>
