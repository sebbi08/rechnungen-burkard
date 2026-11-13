<script setup lang="ts">
import Button from "primevue/button";
import Checkbox from "primevue/checkbox";
import Dialog from "primevue/dialog";
import InputNumber from "primevue/inputnumber";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import type { ItemGraves, PriceIncreaseProblem } from "@/api/endpoints";
import {
  type IncreaseInput,
  useItemPriceIncreasePreview,
} from "@/composables/useItemPriceIncreasePreview";
import { vDecimalPoint } from "@/directives/decimalPoint";
import { formatAmount, formatVatRate } from "@/lib/format";
import {
  type IncreaseRow,
  increaseRows,
  type PendingIncrease,
  raiseByPercent,
  rowChanged,
  rowLabel,
} from "@/lib/priceIncrease";

const props = defineProps<{
  itemId: number;
  graves: ItemGraves;
  initial: PendingIncrease | null;
}>();
const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ apply: [pending: PendingIncrease] }>();

const { t } = useI18n();

const MAX_PROBLEMS = 3;

const percent = ref<number | null>(null);
const rows = ref<IncreaseRow[]>([]);

watch(
  visible,
  (open) => {
    if (open) {
      // a copy: the pending increase stays as it was until "Übernehmen"
      const source = props.initial?.rows ?? increaseRows(props.graves);
      rows.value = source.map((row) => ({ ...row, graveIds: [...row.graveIds] }));
      percent.value = props.initial?.percent ?? null;
    }
  },
  { immediate: true },
);

function onPercent(value: number | null): void {
  if (value === percent.value) {
    return;
  }
  percent.value = value;
  // every price is proposed again, which drops a manual edit
  for (const row of rows.value) {
    row.newPrice = raiseByPercent(row.oldPrice, value ?? 0);
  }
}

function onNewPrice(row: IncreaseRow, value: number | null): void {
  row.newPrice = value ?? row.oldPrice;
}

const input = computed<IncreaseInput | null>(() => {
  const changed = rows.value.filter(rowChanged);
  return changed.length === 0 || !visible.value
    ? null
    : { itemId: props.itemId, rows: changed.map((row) => ({ ...row })) };
});
const { preview, answered, pending, failed, key } = useItemPriceIncreasePreview(input);

// the answer belongs to the input it was asked for: anything else is stale
const fresh = computed(
  () => answered.value != null && JSON.stringify(answered.value) === key.value,
);

function problemsOf(row: IncreaseRow): PriceIncreaseProblem[] {
  if (!rowChanged(row) || !preview.value || !answered.value) {
    return [];
  }
  const index = answered.value.rows.findIndex(
    (candidate) =>
      candidate.isDefault === row.isDefault &&
      candidate.oldPrice === row.oldPrice &&
      candidate.priceIncludesVat === row.priceIncludesVat &&
      candidate.newPrice === row.newPrice,
  );
  return preview.value.Rows[index]?.Problems ?? [];
}

const anyProblems = computed(() => (preview.value?.Rows ?? []).some((r) => r.Problems.length > 0));
const corrections = computed(() => (fresh.value ? (preview.value?.Corrections ?? []) : []));
const canApply = computed(
  () => rows.value.some(rowChanged) && fresh.value && !anyProblems.value && !pending.value,
);

function problemText(problem: PriceIncreaseProblem): string {
  return t("priceIncreaseProblem", [problem.GraveNumber, problem.Invoice]);
}

function apply(): void {
  if (!canApply.value) {
    return;
  }
  emit("apply", {
    percent: percent.value,
    rows: rows.value.map((row) => ({ ...row, graveIds: [...row.graveIds] })),
    corrections: [...corrections.value],
  });
  visible.value = false;
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="t('priceIncrease')"
    :style="{ width: '36rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <div class="flex flex-col gap-4">
      <div class="flex flex-col gap-2">
        <label for="priceIncreasePercent" class="font-medium">
          {{ t("priceIncreasePercent") }}
        </label>
        <InputNumber
          v-decimal-point
          input-id="priceIncreasePercent"
          :model-value="percent"
          locale="de-DE"
          :min="0"
          :min-fraction-digits="0"
          :max-fraction-digits="2"
          autofocus
          @update:model-value="onPercent"
        />
        <small class="text-muted-color">{{ t("priceIncreaseRounded") }}</small>
      </div>

      <div class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-left">
              <th class="py-1 pr-2"></th>
              <th class="py-1 pr-2 font-medium"></th>
              <th class="py-1 pr-2 text-right font-medium">{{ t("priceIncreaseOld") }}</th>
              <th class="py-1 text-right font-medium">{{ t("priceIncreaseNew") }}</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="(row, index) in rows" :key="index">
              <tr :class="problemsOf(row).length > 0 ? 'outline outline-1 outline-red-500' : ''">
                <td class="py-1 pr-2 align-top">
                  <Checkbox v-model="row.ticked" binary :aria-label="rowLabel(row, t)" />
                </td>
                <td class="py-1 pr-2 align-top">
                  {{ rowLabel(row, t) }}
                  <span class="text-muted-color">
                    · {{ t("priceIncreaseGraves", [row.graveCount]) }}
                  </span>
                </td>
                <td
                  class="py-1 pr-2 text-right align-top tabular-nums whitespace-nowrap"
                  :class="row.ticked ? undefined : 'text-muted-color'"
                >
                  {{ formatAmount(row.oldPrice) }}
                </td>
                <td class="py-1 text-right align-top">
                  <InputNumber
                    v-if="row.ticked"
                    v-decimal-point
                    :model-value="row.newPrice"
                    :aria-label="`${t('priceIncreaseNew')} ${rowLabel(row, t)}`"
                    locale="de-DE"
                    :min="0.01"
                    :min-fraction-digits="2"
                    :max-fraction-digits="2"
                    suffix=" €"
                    input-class="w-32 text-right"
                    @update:model-value="onNewPrice(row, $event)"
                  />
                  <span v-else class="text-muted-color tabular-nums">
                    {{ formatAmount(row.oldPrice) }}
                  </span>
                </td>
              </tr>
              <tr v-if="problemsOf(row).length > 0">
                <td></td>
                <td colspan="3" class="pb-2">
                  <ul class="m-0 flex list-none flex-col gap-1 p-0 text-red-700 dark:text-red-400">
                    <li
                      v-for="problem in problemsOf(row).slice(0, MAX_PROBLEMS)"
                      :key="problem.GraveId + problem.Invoice + problem.VatRate"
                    >
                      {{ problemText(problem) }}
                    </li>
                    <li v-if="problemsOf(row).length > MAX_PROBLEMS">
                      {{ t("priceIncreaseMoreProblems", [problemsOf(row).length - MAX_PROBLEMS]) }}
                    </li>
                  </ul>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>

      <ProgressSpinner
        v-if="pending"
        style="width: 1.25rem; height: 1.25rem"
        :stroke-width="12"
        :aria-label="t('billingPreviewChecking')"
      />
      <Message v-if="failed" severity="warn" size="small">
        {{ t("billingPreviewUnavailable") }}
      </Message>
      <Message v-if="corrections.length > 0" severity="warn" size="small">
        <details>
          <summary class="cursor-pointer">
            {{ t("priceIncreaseCorrections", [new Set(corrections.map((c) => c.GraveId)).size]) }}
          </summary>
          <ul class="mt-2 mb-0 flex list-none flex-col gap-1 p-0">
            <li v-for="(c, index) in corrections" :key="index">
              {{ c.GraveNumber }} · {{ c.Invoice }} · {{ c.ItemNumber }} ·
              {{ formatVatRate(c.VatRate) }} % · {{ formatAmount(c.EnteredPrice) }} →
              {{ formatAmount(c.CorrectedPrice) }}
            </li>
          </ul>
        </details>
      </Message>
    </div>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        :label="t('priceIncreaseApply')"
        icon="pi pi-check"
        :disabled="!canApply"
        @click="apply"
      />
    </template>
  </Dialog>
</template>
