<script setup lang="ts">
import Button from "primevue/button";
import Checkbox from "primevue/checkbox";
import Dialog from "primevue/dialog";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import type { Invoice } from "@/api/generated";
import { formatAmount, formatInvoiceNumber } from "@/lib/format";
import { halfYearText } from "@/lib/halfYear";

const props = defineProps<{
  graveNumber: string;
  /** The grave's open invoices, oldest half-year first. */
  invoices: Invoice[];
  busy?: boolean;
}>();
const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ confirm: [invoiceIds: number[]] }>();

const { t } = useI18n();

// Nothing is ticked when the dialog opens: the user ticks who has paid.
const selected = ref(new Set<number>());
watch(visible, (open) => {
  if (open) {
    selected.value = new Set();
  }
});

// one group per recipient, in the order their first open invoice comes
const groups = computed(() => {
  const byRecipient = new Map<number, { name: string; invoices: Invoice[] }>();
  for (const invoice of props.invoices) {
    const group = byRecipient.get(invoice.RecipientId);
    if (group) {
      group.invoices.push(invoice);
    } else {
      byRecipient.set(invoice.RecipientId, { name: invoice.RecipientName, invoices: [invoice] });
    }
  }
  return [...byRecipient.entries()].map(([recipientId, group]) => ({
    recipientId,
    ...group,
    total: sum(group.invoices),
  }));
});

function sum(invoices: Invoice[]): number {
  // in cents, so adding up shares never shows a stray fraction
  return invoices.reduce((cents, invoice) => cents + Math.round(invoice.GrossTotal * 100), 0) / 100;
}

function isSelected(invoice: Invoice): boolean {
  return selected.value.has(invoice.Id);
}

function setSelected(invoices: Invoice[], value: boolean): void {
  const next = new Set(selected.value);
  for (const invoice of invoices) {
    if (value) {
      next.add(invoice.Id);
    } else {
      next.delete(invoice.Id);
    }
  }
  selected.value = next;
}

function groupState(invoices: Invoice[]): "all" | "some" | "none" {
  const count = invoices.filter(isSelected).length;
  return count === 0 ? "none" : count === invoices.length ? "all" : "some";
}

const chosen = computed(() => props.invoices.filter(isSelected));
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="t('markPaidTitle')"
    :style="{ width: '34rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <p class="m-0 mb-3">{{ t("markPaidIntro", [graveNumber]) }}</p>
    <div class="flex flex-col gap-3">
      <section
        v-for="group in groups"
        :key="group.recipientId"
        class="rounded-md border border-surface-200 dark:border-surface-700"
      >
        <div
          class="flex items-center gap-2 rounded-t-md bg-surface-100 px-3 py-2 dark:bg-surface-800"
        >
          <Checkbox
            :model-value="groupState(group.invoices) === 'all'"
            :indeterminate="groupState(group.invoices) === 'some'"
            :input-id="`markPaidRecipient${group.recipientId}`"
            binary
            @update:model-value="setSelected(group.invoices, groupState(group.invoices) !== 'all')"
          />
          <label :for="`markPaidRecipient${group.recipientId}`" class="grow font-medium">
            {{ group.name }}
          </label>
          <span class="font-medium tabular-nums">{{ formatAmount(group.total) }}</span>
        </div>
        <ul class="m-0 list-none p-0">
          <li
            v-for="invoice in group.invoices"
            :key="invoice.Id"
            class="flex items-center gap-2 py-1.5 pr-3 pl-8"
          >
            <Checkbox
              :model-value="isSelected(invoice)"
              :input-id="`markPaidInvoice${invoice.Id}`"
              :aria-label="t('markPaidInvoiceLabel', [formatInvoiceNumber(invoice.InvoiceNumber)])"
              binary
              @update:model-value="setSelected([invoice], !isSelected(invoice))"
            />
            <label :for="`markPaidInvoice${invoice.Id}`" class="flex grow gap-3">
              <span class="w-16 shrink-0">
                {{ halfYearText(invoice.PeriodYear ?? 0, invoice.PeriodHalf ?? 0) }}
              </span>
              <span>{{ formatInvoiceNumber(invoice.InvoiceNumber) }}</span>
            </label>
            <span class="tabular-nums">{{ formatAmount(invoice.GrossTotal) }}</span>
          </li>
        </ul>
      </section>
    </div>
    <p class="m-0 mt-3 text-right font-medium" aria-live="polite">
      {{ t("markPaidSelection", [chosen.length, formatAmount(sum(chosen))]) }}
    </p>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        :label="t('actionMarkPaid')"
        icon="pi pi-wallet"
        :disabled="chosen.length === 0"
        :loading="busy"
        @click="
          emit(
            'confirm',
            chosen.map((invoice) => invoice.Id),
          )
        "
      />
    </template>
  </Dialog>
</template>
