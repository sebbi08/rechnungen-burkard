<script setup lang="ts">
import Button from "primevue/button";
import Select from "primevue/select";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { useRecipientsQuery } from "@/api/queries";
import { formatAddressLine } from "@/lib/format";
import RecipientQuickCreate from "./RecipientQuickCreate.vue";

const props = defineProps<{ inputId?: string; defaultToNewest?: boolean }>();
const model = defineModel<number | null>({ required: true });

const { t } = useI18n();
const recipients = useRecipientsQuery(ref(""));
const quickOpen = ref(false);

// A fresh draft starts on the newest recipient. Taken from
// the same list the Select shows, so the default is always one it can display.
// Not before the refetch on opening has settled: the cached list may predate a
// recipient created since, and the default would never move to it.
watch(
  [() => recipients.data.value, () => recipients.isFetching.value],
  ([list, fetching]) => {
    if (props.defaultToNewest && !fetching && model.value == null && list?.length) {
      model.value = list[0].Id;
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
    <Select
      v-model="model"
      :input-id="inputId"
      :options="recipients.data.value ?? []"
      option-value="Id"
      option-label="Name"
      filter
      :filter-fields="['Name', 'Address', 'Number']"
      :placeholder="t('selectRecipient')"
      :loading="recipients.isFetching.value"
      class="min-w-0 flex-1 basis-64"
    >
      <template #option="{ option }">
        <div class="flex flex-col">
          <span>{{ option.Name }}</span>
          <small class="text-muted-color">{{ formatAddressLine(option.Address) }}</small>
        </div>
      </template>
    </Select>
    <Button :label="t('newRecipient')" icon="pi pi-user-plus" outlined @click="quickOpen = true" />
    <RecipientQuickCreate v-model:visible="quickOpen" @created="onCreated" />
  </div>
</template>
