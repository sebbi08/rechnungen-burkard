<script setup lang="ts">
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import type { ColumnFilterField } from "@/composables/useColumnFilters";

/** One column's filter control: a text field, or a select that starts with
 *  "Alle". It goes into a Column's `#filter` slot -- PrimeVue's `filterModel`
 *  is not used, the value comes from `useColumnFilters` -- and into the
 *  phone's filter sheet. The control is named after its column either way,
 *  "Name durchsuchen" or "Zahlung filtern". */
const props = defineProps<{
  field: ColumnFilterField;
  /** For a label pointing at the control. */
  inputId?: string;
}>();
const model = defineModel<string>({ required: true });

const { t } = useI18n();

// "Alle" stands for the empty value, which PrimeVue's Select takes for no
// value at all and so shows no label for
const ALL = "\u0000";

const options = computed(() => [
  { value: ALL, label: t("filterAll") },
  ...(props.field.options ?? []).filter((option) => option.value !== ""),
]);

// a stored value that is no longer among the options (a deleted cemetery, an
// edited link) filters nothing, and the select says so
const selected = computed({
  get: () =>
    model.value !== "" && options.value.some((option) => option.value === model.value)
      ? model.value
      : ALL,
  set: (value: string | null) => {
    model.value = value == null || value === ALL ? "" : value;
  },
});
</script>

<template>
  <Select
    v-if="field.kind === 'select'"
    v-model="selected"
    :options="options"
    option-label="label"
    option-value="value"
    :input-id="inputId"
    :aria-label="t('filterSelectColumn', [field.header])"
    class="w-full min-w-0"
  />
  <InputText
    v-else
    :id="inputId"
    v-model="model"
    :aria-label="t('filterSearchColumn', [field.header])"
    class="w-full min-w-0"
  />
</template>
