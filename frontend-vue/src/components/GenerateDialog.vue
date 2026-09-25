<script setup lang="ts">
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import Select from "primevue/select";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { yearOptions } from "@/lib/halfYear";

const props = defineProps<{ initialYear: number; initialHalf: number }>();
const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ generate: [year: number, half: number] }>();

const { t } = useI18n();
// the same range as the overview's half-year select, which it starts on
const years = yearOptions(2, 1).map((option) => ({
  value: Number(option.key),
  label: option.text,
}));
const halves = [
  { value: 1, label: "H1" },
  { value: 2, label: "H2" },
];
const year = ref(props.initialYear);
const half = ref(props.initialHalf);

watch(visible, (open) => {
  if (open) {
    year.value = props.initialYear;
    half.value = props.initialHalf;
  }
});
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
        <Select
          v-model="year"
          input-id="generateYear"
          :options="years"
          option-label="label"
          option-value="value"
        />
      </div>
      <div class="flex flex-col gap-2">
        <label for="generateHalf" class="font-medium">{{ t("halfYear") }}</label>
        <Select
          v-model="half"
          input-id="generateHalf"
          :options="halves"
          option-label="label"
          option-value="value"
        />
      </div>
    </div>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button :label="t('generateNow')" icon="pi pi-bolt" @click="emit('generate', year, half)" />
    </template>
  </Dialog>
</template>
