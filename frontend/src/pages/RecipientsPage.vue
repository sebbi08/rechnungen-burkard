<script setup lang="ts">
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import InputText from "primevue/inputtext";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import type { Recipient } from "@/api/generated";
import { useRecipientsPageQuery } from "@/api/queries";
import PageHeader from "@/components/PageHeader.vue";
import RecipientDialog from "@/components/RecipientDialog.vue";
import { useDebounced } from "@/composables/useDebounced";
import { usePageReport } from "@/composables/usePageReport";
import { lastPageStart, PAGE_SIZE, PAGINATOR_TEMPLATE } from "@/lib/paging";

const { t } = useI18n();
const search = ref("");
const term = useDebounced(search);
const first = ref(0);

// a new search starts on page one
watch(term, () => {
  first.value = 0;
});

const recipients = useRecipientsPageQuery(term, first);

// a page past the end of a list that shrank moves back to the last page that has rows
watch(
  () => recipients.data.value?.total,
  (total) => {
    if (total != null && total > 0 && first.value >= total) {
      first.value = lastPageStart(total);
    }
  },
);

const pageReport = usePageReport();

const dialogOpen = ref(false);
const editing = ref<Recipient | null>(null);

function open(recipient: Recipient | null): void {
  editing.value = recipient;
  dialogOpen.value = true;
}
</script>

<template>
  <PageHeader :title="t('menuRecipients')">
    <template #actions>
      <Button :label="t('createRecipient')" icon="pi pi-plus" @click="open(null)" />
    </template>
  </PageHeader>

  <Card>
    <template #content>
      <div class="mb-4">
        <IconField class="w-full md:w-80">
          <InputIcon class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('searchByName')"
            :aria-label="t('searchByName')"
            class="w-full"
          />
        </IconField>
      </div>
      <DataTable
        :value="recipients.data.value?.rows ?? []"
        data-key="Id"
        lazy
        paginator
        :rows="PAGE_SIZE"
        :first="first"
        :total-records="recipients.data.value?.total ?? 0"
        :paginator-template="PAGINATOR_TEMPLATE"
        :current-page-report-template="pageReport"
        :loading="recipients.isFetching.value"
        striped-rows
        @page="first = $event.first"
      >
        <template #empty>{{ t("noData") }}</template>
        <Column
          field="Number"
          :header="t('recipientNumber')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell"
        />
        <Column :header="t('recipientName')">
          <template #body="{ data }">
            {{ data.Name }}
            <!-- on a phone the number sits under the name instead of in its own column -->
            <div class="text-sm text-muted-color sm:hidden">{{ data.Number }}</div>
          </template>
        </Column>
        <Column
          :header="t('recipientAddress')"
          header-class="hidden md:table-cell"
          body-class="hidden whitespace-pre-line md:table-cell"
        >
          <template #body="{ data }">{{ data.Address }}</template>
        </Column>
        <Column
          :header="t('recipientEmail')"
          header-class="hidden lg:table-cell"
          body-class="hidden lg:table-cell [overflow-wrap:anywhere]"
        >
          <template #body="{ data }">{{ data.Email ?? "" }}</template>
        </Column>
        <Column class="w-16 text-right">
          <template #body="{ data }">
            <Button
              v-tooltip.left="t('edit')"
              icon="pi pi-pencil"
              text
              rounded
              :aria-label="`${t('edit')}: ${data.Name}`"
              @click="open(data)"
            />
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <RecipientDialog v-model:visible="dialogOpen" :recipient="editing" />
</template>
