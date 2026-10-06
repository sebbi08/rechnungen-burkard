<script setup lang="ts">
import Button from "primevue/button";
import DatePicker from "primevue/datepicker";
import Dialog from "primevue/dialog";
import SelectButton from "primevue/selectbutton";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import {
  currentHalfYear,
  earliestGenerationHalfYear,
  halfYearBefore,
  halfYearText,
} from "@/lib/halfYear";

const props = defineProps<{ initialYear: number; initialHalf: number }>();
const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ generate: [year: number, half: number] }>();

const { t } = useI18n();
// the backend refuses anything older; the dialog says so before it is asked
const [earliestYear, earliestHalf] = earliestGenerationHalfYear();
const minDate = new Date(earliestYear, 0, 1);

const year = ref<Date | null>(null);
const half = ref(1);

const halves = computed(() => [
  {
    value: 1,
    label: "H1",
    disabled: year.value?.getFullYear() === earliestYear && earliestHalf > 1,
  },
  { value: 2, label: "H2", disabled: false },
]);
const tooOld = computed(
  () =>
    year.value != null &&
    halfYearBefore(year.value.getFullYear(), half.value, earliestYear, earliestHalf),
);
const canGenerate = computed(() => year.value != null && !tooOld.value);

watch(visible, (open) => {
  if (!open) {
    return;
  }
  // the overview's half-year, unless that is older than a run may reach
  const [initialYear, initialHalf] = halfYearBefore(
    props.initialYear,
    props.initialHalf,
    earliestYear,
    earliestHalf,
  )
    ? currentHalfYear()
    : [props.initialYear, props.initialHalf];
  year.value = new Date(initialYear, 0, 1);
  half.value = initialHalf;
});

function generate(): void {
  if (year.value) {
    emit("generate", year.value.getFullYear(), half.value);
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="t('generateInvoices')"
    :style="{ width: '22rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <div class="grid grid-cols-2 gap-4">
      <div class="flex flex-col gap-2">
        <label for="generateYear" class="font-medium">{{ t("year") }}</label>
        <DatePicker
          v-model="year"
          input-id="generateYear"
          view="year"
          date-format="yy"
          :min-date="minDate"
          show-icon
          fluid
        />
      </div>
      <div class="flex flex-col gap-2">
        <span class="font-medium">{{ t("halfYear") }}</span>
        <SelectButton
          v-model="half"
          :options="halves"
          option-label="label"
          option-value="value"
          option-disabled="disabled"
          :allow-empty="false"
          :aria-label="t('halfYear')"
        />
      </div>
    </div>
    <p class="mt-4 mb-0 text-sm text-muted-color">
      {{ t("generateEarliest", [halfYearText(earliestYear, earliestHalf)]) }}
    </p>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        :label="t('generateNow')"
        icon="pi pi-bolt"
        :disabled="!canGenerate"
        @click="generate"
      />
    </template>
  </Dialog>
</template>
