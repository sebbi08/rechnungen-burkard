<script setup lang="ts">
import { keepPreviousData, useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import Tag from "primevue/tag";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink } from "vue-router";
import { type Quote, QuoteState } from "@/api/generated";
import { odata } from "@/api/odata";
import ColumnFilterCell from "@/components/ColumnFilterCell.vue";
import ColumnFilterSheet from "@/components/ColumnFilterSheet.vue";
import InvoiceEditor from "@/components/InvoiceEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import QuoteRowActions from "@/components/QuoteRowActions.vue";
import QuoteStateTag from "@/components/QuoteStateTag.vue";
import { type ColumnFilterField, useColumnFilters } from "@/composables/useColumnFilters";
import { usePageReport } from "@/composables/usePageReport";
import { formatAmount, formatDate, formatInvoiceNumber, formatRevisionBadge } from "@/lib/format";
import { lastPageStart, PAGE_SIZE, PAGINATOR_TEMPLATE } from "@/lib/paging";

const { t } = useI18n();
const filters = useColumnFilters({ number: "", recipient: "", date: "", total: "", state: "" });
const first = ref(0);

// a new search or filter starts on page one
watch(filters.applied, () => {
  first.value = 0;
});

const stateOptions = computed(() => [
  { value: QuoteState.Open, label: t("quoteStateOpen") },
  { value: QuoteState.Declined, label: t("quoteStateDeclined") },
  { value: QuoteState.Converted, label: t("quoteStateConverted") },
]);

// the sheet lists every searchable column, the ones a narrow table hides too
const fields = computed<ColumnFilterField[]>(() => [
  { key: "number", header: t("quoteNumber"), kind: "text" },
  { key: "recipient", header: t("graveRecipients"), kind: "text" },
  { key: "date", header: t("invoiceDate"), kind: "text" },
  { key: "total", header: t("invoiceTotal"), kind: "text" },
  { key: "state", header: t("quoteState"), kind: "select", options: stateOptions.value },
]);

function field(key: string): ColumnFilterField {
  return fields.value.find((each) => each.key === key)!;
}

const quotes = useQuery({
  queryKey: ["quotes", "list", filters.applied, first],
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
    // a value that is no state counts as "Alle"
    const state = Object.values(QuoteState).find(
      (each) => String(each) === filters.applied.value.state,
    );
    const response = await odata
      .Quotes()
      .query((b, q) => {
        let filter = q.IsLatestRevision.isTrue();
        if (state) {
          filter = filter.and(q.State.equals(state));
        }
        b.filter(filter).orderBy(q.Id.desc()).skip(first.value).top(PAGE_SIZE).count();
      })
      .execute({ params });
    return { rows: response.data.value, total: response.data["@odata.count"] ?? 0 };
  },
  placeholderData: keepPreviousData,
});

// a page that emptied -- its last quote deleted, or declined under "Offen" --
// moves back to the last page that has rows
watch(
  () => quotes.data.value?.total,
  (total) => {
    if (total != null && total > 0 && first.value >= total) {
      first.value = lastPageStart(total);
    }
  },
);

const pageReport = usePageReport();

const editorOpen = ref(false);
const editorTarget = ref<{ id: number; recipientId: number } | null>(null);

function openEditor(quote: Quote | null): void {
  editorTarget.value = quote ? { id: quote.Id, recipientId: quote.RecipientId } : null;
  editorOpen.value = true;
}
</script>

<template>
  <PageHeader :title="t('menuQuotes')">
    <template #actions>
      <Button :label="t('createQuote')" icon="pi pi-plus" @click="openEditor(null)" />
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
        :value="quotes.data.value?.rows ?? []"
        data-key="Id"
        lazy
        paginator
        :rows="PAGE_SIZE"
        :first="first"
        :total-records="quotes.data.value?.total ?? 0"
        :paginator-template="PAGINATOR_TEMPLATE"
        :current-page-report-template="pageReport"
        :loading="quotes.isFetching.value"
        striped-rows
        filter-display="row"
        :filters="{}"
        class="column-filters-md compact-cells"
        @page="first = $event.first"
      >
        <template #empty>{{ t("noData") }}</template>
        <Column :header="t('quoteNumber')" :show-filter-menu="false" :show-clear-button="false">
          <template #body="{ data }">
            <span class="font-medium whitespace-nowrap tabular-nums">{{
              formatInvoiceNumber(data.QuoteNumber)
            }}</span>
            <Tag
              v-if="data.RevisionNumber > 0"
              v-tooltip.top="t('revision')"
              :value="formatRevisionBadge(data.RevisionNumber)"
              severity="secondary"
              class="ml-2"
            />
            <!-- on a phone the name and the state sit under the number instead
                 of in columns of their own -->
            <div class="text-sm text-muted-color sm:hidden">{{ data.RecipientName }}</div>
            <div class="mt-1 sm:hidden"><QuoteStateTag :state="data.State" /></div>
            <RouterLink
              v-if="data.InvoiceId != null && data.InvoiceNumber"
              :to="`/invoices/${data.InvoiceId}`"
              class="text-sm whitespace-nowrap text-primary hover:underline sm:hidden"
            >
              {{ t("quoteInvoiceLink", [formatInvoiceNumber(data.InvoiceNumber)]) }}
            </RouterLink>
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
          :header="t('quoteState')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">
            <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
              <QuoteStateTag :state="data.State" />
              <!-- the invoice the quote became, under its first number -->
              <RouterLink
                v-if="data.InvoiceId != null && data.InvoiceNumber"
                :to="`/invoices/${data.InvoiceId}`"
                class="text-sm whitespace-nowrap text-primary hover:underline"
              >
                {{ t("quoteInvoiceLink", [formatInvoiceNumber(data.InvoiceNumber)]) }}
              </RouterLink>
            </div>
          </template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.state" :field="field('state')" />
          </template>
        </Column>
        <Column class="w-0 whitespace-nowrap">
          <template #body="{ data }">
            <QuoteRowActions
              :quote-id="data.Id"
              :state="data.State"
              :details-to="`/quotes/${data.RootId}`"
              :number="formatInvoiceNumber(data.QuoteNumber)"
              @edit="openEditor(data)"
            />
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <InvoiceEditor v-model:visible="editorOpen" :target="editorTarget" kind="quote" />
</template>
