<script setup lang="ts">
import { keepPreviousData, useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import Tag from "primevue/tag";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import type { Invoice } from "@/api/generated";
import { odata } from "@/api/odata";
import ColumnFilterCell from "@/components/ColumnFilterCell.vue";
import ColumnFilterSheet from "@/components/ColumnFilterSheet.vue";
import InvoiceEditor from "@/components/InvoiceEditor.vue";
import InvoiceRowActions from "@/components/InvoiceRowActions.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import { type ColumnFilterField, useColumnFilters } from "@/composables/useColumnFilters";
import { usePageReport } from "@/composables/usePageReport";
import { formatAmount, formatDate, formatInvoiceNumber, formatRevisionBadge } from "@/lib/format";
import { lastPageStart, PAGE_SIZE, PAGINATOR_TEMPLATE } from "@/lib/paging";

const { t } = useI18n();
const filters = useColumnFilters({ number: "", recipient: "", date: "", total: "", paid: "" });
const first = ref(0);

// a new search or filter starts on page one
watch(filters.applied, () => {
  first.value = 0;
});

const paymentOptions = computed(() => [
  { value: "unpaid", label: t("paymentFilterUnpaid") },
  { value: "paid", label: t("paymentFilterPaid") },
]);

// the sheet lists every searchable column, the ones a narrow table hides too
const fields = computed<ColumnFilterField[]>(() => [
  { key: "number", header: t("invoiceNumber"), kind: "text" },
  { key: "recipient", header: t("graveRecipients"), kind: "text" },
  { key: "date", header: t("invoiceDate"), kind: "text" },
  { key: "total", header: t("invoiceTotal"), kind: "text" },
  { key: "paid", header: t("paidStatus"), kind: "select", options: paymentOptions.value },
]);

function field(key: string): ColumnFilterField {
  return fields.value.find((each) => each.key === key)!;
}

const invoices = useQuery({
  queryKey: ["invoices", "list", filters.applied, first],
  queryFn: async () => {
    // the text filters go out as the backend's own query options, trimmed;
    // empty ones are left out
    const params: Record<string, string> = {};
    for (const key of ["number", "recipient", "date", "total"] as const) {
      const value = filters.applied.value[key].trim();
      if (value !== "") {
        params[key] = value;
      }
    }
    // a value that is no option counts as "Alle"
    const paid = filters.applied.value.paid;
    const response = await odata
      .Invoices()
      .query((b, q) => {
        // ordinary invoices only; grave invoices are reached through their grave
        let filter = q.GraveId.isNull().and(q.IsLatestRevision.isTrue());
        if (paid === "unpaid") {
          filter = filter.and(q.PaidOn.isNull());
        } else if (paid === "paid") {
          filter = filter.and(q.PaidOn.isNotNull());
        }
        b.filter(filter).orderBy(q.Id.desc()).skip(first.value).top(PAGE_SIZE).count();
      })
      .execute({ params });
    return { rows: response.data.value, total: response.data["@odata.count"] ?? 0 };
  },
  placeholderData: keepPreviousData,
});

// a page that emptied -- its last unpaid invoice marked paid under "unpaid
// only" -- moves back to the last page that has rows
watch(
  () => invoices.data.value?.total,
  (total) => {
    if (total != null && total > 0 && first.value >= total) {
      first.value = lastPageStart(total);
    }
  },
);

const pageReport = usePageReport();

const editorOpen = ref(false);
const editorTarget = ref<{ invoiceId: number; recipientId: number } | null>(null);

function openEditor(invoice: Invoice | null): void {
  editorTarget.value = invoice ? { invoiceId: invoice.Id, recipientId: invoice.RecipientId } : null;
  editorOpen.value = true;
}
</script>

<template>
  <PageHeader :title="t('menuInvoices')">
    <template #actions>
      <Button :label="t('createInvoice')" icon="pi pi-plus" @click="openEditor(null)" />
    </template>
  </PageHeader>

  <Card>
    <template #content>
      <ColumnFilterSheet
        v-model:values="filters.values"
        :fields="fields"
        breakpoint="md"
        @clear="filters.clear()"
      />

      <DataTable
        :value="invoices.data.value?.rows ?? []"
        data-key="Id"
        lazy
        paginator
        :rows="PAGE_SIZE"
        :first="first"
        :total-records="invoices.data.value?.total ?? 0"
        :paginator-template="PAGINATOR_TEMPLATE"
        :current-page-report-template="pageReport"
        :loading="invoices.isFetching.value"
        striped-rows
        filter-display="row"
        :filters="{}"
        class="column-filters-md"
        @page="first = $event.first"
      >
        <template #empty>{{ t("noData") }}</template>
        <Column :header="t('invoiceNumber')" :show-filter-menu="false" :show-clear-button="false">
          <template #body="{ data }">
            <span class="font-medium whitespace-nowrap tabular-nums">{{
              formatInvoiceNumber(data.InvoiceNumber)
            }}</span>
            <Tag
              v-if="data.RevisionNumber > 0"
              v-tooltip.top="t('revision')"
              :value="formatRevisionBadge(data.RevisionNumber)"
              severity="secondary"
              class="ml-2"
            />
            <!-- on a phone the name and the payment state sit under the number
                 instead of in columns of their own -->
            <div class="text-sm text-muted-color sm:hidden">{{ data.RecipientName }}</div>
            <div class="mt-1 sm:hidden"><PaidStatusTag :paid-on="data.PaidOn" /></div>
          </template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.number" :field="field('number')" />
          </template>
        </Column>
        <Column
          field="RecipientName"
          :header="t('graveRecipients')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell [overflow-wrap:anywhere]"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #filter>
            <ColumnFilterCell v-model="filters.values.recipient" :field="field('recipient')" />
          </template>
        </Column>
        <Column
          :header="t('invoiceDate')"
          header-class="hidden md:table-cell"
          body-class="hidden md:table-cell tabular-nums"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ formatDate(data.GeneratedAt) }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.date" :field="field('date')" />
          </template>
        </Column>
        <Column
          :header="t('invoiceTotal')"
          header-class="hidden md:table-cell col-num"
          body-class="hidden text-right whitespace-nowrap tabular-nums md:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ formatAmount(data.GrossTotal) }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.total" :field="field('total')" />
          </template>
        </Column>
        <Column
          :header="t('paidStatus')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }"><PaidStatusTag :paid-on="data.PaidOn" /></template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.paid" :field="field('paid')" />
          </template>
        </Column>
        <Column class="w-0 whitespace-nowrap">
          <template #body="{ data }">
            <InvoiceRowActions
              :invoice-id="data.Id"
              :paid-on="data.PaidOn"
              :details-to="`/invoices/${data.RootId}`"
              :row-label="formatInvoiceNumber(data.InvoiceNumber)"
              @edit="openEditor(data)"
            />
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <InvoiceEditor v-model:visible="editorOpen" :target="editorTarget" />
</template>
