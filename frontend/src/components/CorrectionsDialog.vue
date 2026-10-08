<script setup lang="ts">
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import { useI18n } from "vue-i18n";
import { formatAmount, formatVatRate } from "@/lib/format";

export interface CorrectionRow {
  invoice: string;
  item: string;
  vatRate: number;
  entered: number;
  corrected: number;
}

defineProps<{ rows: CorrectionRow[]; saving?: boolean }>();
const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ confirm: [] }>();
const { t } = useI18n();
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="t('correctionsTitle')"
    :style="{ width: '40rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <p class="mt-0">{{ t("correctionsIntro") }}</p>
    <div class="overflow-x-auto">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-left text-muted-color">
            <th class="py-1 pr-2 font-medium">{{ t("correctionsInvoice") }}</th>
            <th class="py-1 pr-2 font-medium">{{ t("correctionsItem") }}</th>
            <th class="py-1 pr-2 font-medium">{{ t("vatRate") }}</th>
            <th class="py-1 pr-2 text-right font-medium">{{ t("correctionsEntered") }}</th>
            <th class="py-1 text-right font-medium">{{ t("correctionsCorrected") }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, index) in rows" :key="index">
            <td class="py-1 pr-2">{{ row.invoice }}</td>
            <td class="py-1 pr-2">{{ row.item }}</td>
            <td class="py-1 pr-2 whitespace-nowrap">{{ formatVatRate(row.vatRate) }} %</td>
            <td class="py-1 pr-2 text-right tabular-nums whitespace-nowrap">
              {{ formatAmount(row.entered) }}
            </td>
            <td
              class="py-1 text-right font-semibold tabular-nums whitespace-nowrap text-orange-700 dark:text-orange-400"
            >
              {{ formatAmount(row.corrected) }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        :label="t('correctionsConfirm')"
        icon="pi pi-check"
        severity="warn"
        :loading="saving"
        @click="emit('confirm')"
      />
    </template>
  </Dialog>
</template>
