<script setup lang="ts">
import Select from "primevue/select";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import { useItemsQuery } from "@/api/queries";
import { useDebounced } from "@/composables/useDebounced";
import { SERVER_FILTER } from "@/lib/serverFilter";

const model = defineModel<number | null>({ required: true });

const { t } = useI18n();

// The Select's own filter field searches on the server, as the recipient
// picker's does. With nothing typed that is the whole catalogue, the cache
// entry the line editor reads its captions and prices from.
const filter = ref("");
const items = useItemsQuery(useDebounced(filter));
</script>

<template>
  <Select
    v-model="model"
    :options="items.data.value ?? []"
    option-value="Id"
    option-label="Number"
    filter
    :filter-match-mode="SERVER_FILTER"
    :placeholder="t('selectItem')"
    fluid
    overlay-class="max-w-[calc(100vw-1rem)]"
    @filter="filter = $event.value"
  >
    <!-- A long number has no space to break at. Without the width limit and
         the break, it widened the list past the screen of a phone, which
         then zoomed the whole page out and slid the editor under it. -->
    <template #option="{ option }">
      <span class="min-w-0 [overflow-wrap:anywhere]">
        <span class="font-medium">{{ option.Number }}</span>
        <span class="ml-2 text-muted-color">{{ option.Description }}</span>
      </span>
    </template>
  </Select>
</template>
