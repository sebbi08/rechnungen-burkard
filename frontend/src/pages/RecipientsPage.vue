<script setup lang="ts">
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import type { Recipient } from "@/api/generated";
import { useRecipientsPageQuery } from "@/api/queries";
import ColumnFilterCell from "@/components/ColumnFilterCell.vue";
import ColumnFilterSheet from "@/components/ColumnFilterSheet.vue";
import PageHeader from "@/components/PageHeader.vue";
import RecipientDialog from "@/components/RecipientDialog.vue";
import { type ColumnFilterField, useColumnFilters } from "@/composables/useColumnFilters";
import { usePageReport } from "@/composables/usePageReport";
import { lastPageStart, PAGE_SIZE, PAGINATOR_TEMPLATE } from "@/lib/paging";

const { t } = useI18n();
const filters = useColumnFilters({ number: "", name: "", address: "", email: "" });
const first = ref(0);

// a new search starts on page one
watch(filters.applied, () => {
  first.value = 0;
});

const recipients = useRecipientsPageQuery(filters.applied, first);

// the sheet lists every searchable column, the ones a narrow table hides too
const fields = computed<ColumnFilterField[]>(() => [
  { key: "number", header: t("recipientNumber"), kind: "text" },
  { key: "name", header: t("recipientName"), kind: "text" },
  { key: "address", header: t("recipientAddress"), kind: "text" },
  { key: "email", header: t("recipientEmail"), kind: "text" },
]);

function field(key: string): ColumnFilterField {
  return fields.value.find((each) => each.key === key)!;
}

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
      <ColumnFilterSheet
        v-model:values="filters.values"
        :fields="fields"
        breakpoint="lg"
        @clear="filters.clear()"
      />
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
        filter-display="row"
        :filters="{}"
        class="column-filters-lg"
        @page="first = $event.first"
      >
        <template #empty>{{ t("noData") }}</template>
        <Column
          field="Number"
          :header="t('recipientNumber')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #filter>
            <ColumnFilterCell v-model="filters.values.number" :field="field('number')" />
          </template>
        </Column>
        <Column :header="t('recipientName')" :show-filter-menu="false" :show-clear-button="false">
          <template #body="{ data }">
            {{ data.Name }}
            <!-- on a phone the number sits under the name instead of in its own column -->
            <div class="text-sm text-muted-color sm:hidden">{{ data.Number }}</div>
          </template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.name" :field="field('name')" />
          </template>
        </Column>
        <Column
          :header="t('recipientAddress')"
          header-class="hidden md:table-cell"
          body-class="hidden whitespace-pre-line md:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ data.Address }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.address" :field="field('address')" />
          </template>
        </Column>
        <Column
          :header="t('recipientEmail')"
          header-class="hidden lg:table-cell"
          body-class="hidden lg:table-cell [overflow-wrap:anywhere]"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ data.Email ?? "" }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.email" :field="field('email')" />
          </template>
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
