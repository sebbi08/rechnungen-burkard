<script setup lang="ts" generic="K extends string">
import Button from "primevue/button";
import Drawer from "primevue/drawer";
import { computed, ref, useId, watch } from "vue";
import { useI18n } from "vue-i18n";
import ColumnFilterCell from "@/components/ColumnFilterCell.vue";
import type { ColumnFilterField } from "@/composables/useColumnFilters";
import { useDrawerFocus } from "@/composables/useDrawerFocus";

/** A list's filters where its table hides columns, and with them the filter
 *  row: a "Filter" button counting the filled fields, a chip per filled field
 *  that empties it, and a sheet from the bottom with a labelled field per
 *  searchable column, hidden columns included. It edits the same values as
 *  the filter row and hides itself from `breakpoint` on, where the row takes
 *  over. `clear` asks the page to empty every field. */
const props = defineProps<{
  fields: ColumnFilterField[];
  breakpoint: "md" | "lg" | "xl";
}>();
const values = defineModel<Record<K, string>>("values", { required: true });
const emit = defineEmits<{ clear: [] }>();

const { t } = useI18n();
const id = useId();

// literal class names: Tailwind only generates the classes it finds written out
const HIDDEN_FROM = { md: "md:hidden", lg: "lg:hidden", xl: "xl:hidden" } as const;

function valueOf(field: ColumnFilterField): string {
  const all: Partial<Record<string, string>> = values.value;
  return all[field.key] ?? "";
}

function set(field: ColumnFilterField, value: string): void {
  values.value = { ...values.value, [field.key]: value };
}

/** What a field filters by, as its chip shows it: nothing for an empty text
 *  or "Alle" -- and for a select value that is not among the options, which
 *  the select shows and the page applies as "Alle". */
function shown(field: ColumnFilterField): string | null {
  const value = valueOf(field);
  if (field.kind === "select") {
    return value === ""
      ? null
      : (field.options?.find((option) => option.value === value)?.label ?? null);
  }
  const trimmed = value.trim();
  return trimmed === "" ? null : trimmed;
}

const chips = computed(() =>
  props.fields.flatMap((field) => {
    const text = shown(field);
    return text == null ? [] : [{ field, text }];
  }),
);

const open = ref(false);
const formEl = ref<HTMLElement | null>(null);
const drawerFocus = useDrawerFocus();
watch(open, (isOpen) => {
  if (isOpen) {
    drawerFocus.remember();
  }
});
</script>

<template>
  <div :class="['mb-4 flex min-w-0 flex-wrap items-center gap-2', HIDDEN_FROM[breakpoint]]">
    <Button
      :label="t('filterButton')"
      icon="pi pi-filter"
      :badge="chips.length > 0 ? String(chips.length) : undefined"
      severity="secondary"
      outlined
      @click="open = true"
    />
    <span
      v-for="chip in chips"
      :key="chip.field.key"
      class="inline-flex max-w-full min-w-0 items-center gap-1 rounded-full bg-emphasis py-1 pr-1 pl-3 text-sm"
    >
      <span class="min-w-0 [overflow-wrap:anywhere]">{{ chip.field.header }}: {{ chip.text }}</span>
      <Button
        icon="pi pi-times"
        text
        rounded
        size="small"
        severity="secondary"
        class="h-7 w-7 shrink-0"
        :aria-label="t('filterRemove', [chip.field.header])"
        @click="set(chip.field, '')"
      />
    </span>

    <!-- Escape is the sheet's own only when it comes from inside it, and not
         from a select whose list is open: that Escape closes just the list -->
    <Drawer
      v-model:visible="open"
      position="bottom"
      :close-on-escape="false"
      :header="t('filterSheetTitle')"
      :aria-label="t('filterSheetTitle')"
      :style="{ height: 'auto', maxHeight: '85dvh' }"
      @after-show="drawerFocus.focusFirst(formEl)"
      @after-hide="drawerFocus.restore()"
      @keydown.capture="drawerFocus.onKeydown($event, () => (open = false))"
    >
      <div ref="formEl" class="flex flex-col gap-4">
        <div class="grid gap-4 sm:grid-cols-2">
          <div v-for="field in fields" :key="field.key" class="flex min-w-0 flex-col gap-2">
            <label :for="`${id}-${field.key}`" class="font-medium">{{ field.header }}</label>
            <ColumnFilterCell
              :field="field"
              :input-id="`${id}-${field.key}`"
              :model-value="valueOf(field)"
              @update:model-value="set(field, $event)"
            />
          </div>
        </div>
        <Button
          :label="t('filterReset')"
          icon="pi pi-filter-slash"
          severity="secondary"
          text
          class="self-start"
          @click="emit('clear')"
        />
      </div>
    </Drawer>
  </div>
</template>
