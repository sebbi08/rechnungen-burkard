<script setup lang="ts">
import Button from "primevue/button";
import Select from "primevue/select";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import type { Recipient } from "@/api/generated";
import { RECIPIENT_PICKER_SIZE, useRecipientPickerQuery, useRecipientQuery } from "@/api/queries";
import { useDebounced } from "@/composables/useDebounced";
import { formatAddressLine } from "@/lib/format";
import RecipientQuickCreate from "./RecipientQuickCreate.vue";

const props = defineProps<{
  inputId?: string;
  defaultToNewest?: boolean;
  /** Recipients the picker must not offer: the ones the form already holds. */
  exclude?: number[];
}>();
const model = defineModel<number | null>({ required: true });
/** The recipient behind the model, for a form that shows more than its id. */
const emit = defineEmits<{ selected: [recipient: Recipient | null] }>();

const { t } = useI18n();
const quickOpen = ref(false);

// The Select's own filter field searches on the server: it only ever holds the
// newest few of the recipients, and narrows them by what is typed.
const filter = ref("");
const term = useDebounced(filter);
const offered = useRecipientPickerQuery(
  term,
  computed(() => props.exclude ?? []),
);

// The pick stays in the list when the search, or twenty newer recipients, have
// pushed it out: a Select shows nothing for a value that is not an option.
const pickedElsewhere = computed(() => {
  const rows = offered.data.value?.rows;
  const id = model.value;
  return id != null && rows && !rows.some((row) => row.Id === id) ? id : null;
});
const picked = useRecipientQuery(pickedElsewhere);
const options = computed(() => {
  const rows = offered.data.value?.rows ?? [];
  const extra = picked.data.value;
  return extra && extra.Id === pickedElsewhere.value ? [extra, ...rows] : rows;
});
const more = computed(
  () => (offered.data.value?.total ?? 0) > (offered.data.value?.rows.length ?? 0),
);

watch(
  () => options.value.find((option) => option.Id === model.value) ?? null,
  (recipient) => emit("selected", recipient),
  { immediate: true },
);

// A fresh draft starts on the newest recipient. Taken from
// the same list the Select shows, so the default is always one it can display.
// Not before the refetch on opening has settled: the cached list may predate a
// recipient created since, and the default would never move to it. The field
// is watched too: an editor clears it on reopening, and the default has to
// follow even when the list itself has not been fetched again. Never while a
// search is typed: its first hit is not a default.
watch(
  [() => offered.data.value?.rows, () => offered.isFetching.value, model, filter, term],
  ([rows, fetching]) => {
    const searching = filter.value.trim() !== "" || term.value.trim() !== "";
    if (props.defaultToNewest && !fetching && !searching && model.value == null && rows?.length) {
      model.value = rows[0].Id;
    }
  },
  { immediate: true },
);

function onCreated(id: number): void {
  model.value = id;
}
</script>

<template>
  <div class="flex flex-wrap items-center gap-2">
    <!-- loading only until a search is typed: the Select ignores clicks while
         it shows its loading icon, and every keystroke fetches -->
    <Select
      v-model="model"
      :input-id="inputId"
      :options="options"
      option-value="Id"
      option-label="Name"
      filter
      :filter-fields="['Name', 'Address', 'Number']"
      :placeholder="t('selectRecipient')"
      :loading="offered.isFetching.value && filter === ''"
      class="min-w-0 flex-1 basis-64"
      overlay-class="max-w-[calc(100vw-1rem)]"
      @filter="filter = $event.value"
    >
      <!-- An option does not wrap on its own. Without the width limit and the
           break, a long name widened the list past the screen of a phone, which
           then zoomed the whole page out and slid the editor under it. -->
      <template #option="{ option }">
        <div class="flex min-w-0 flex-col [overflow-wrap:anywhere] whitespace-normal">
          <span>{{ option.Name }}</span>
          <small class="text-muted-color">{{ formatAddressLine(option.Address) }}</small>
        </div>
      </template>
      <template v-if="more" #footer>
        <div class="px-3 py-2 text-sm text-muted-color">
          {{ t("recipientPickerMore", [RECIPIENT_PICKER_SIZE]) }}
        </div>
      </template>
    </Select>
    <Button :label="t('newRecipient')" icon="pi pi-user-plus" outlined @click="quickOpen = true" />
    <RecipientQuickCreate v-model:visible="quickOpen" @created="onCreated" />
  </div>
</template>
