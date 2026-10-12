<script setup lang="ts">
import { keepPreviousData, useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import Tag from "primevue/tag";
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import type { Invoice } from "@/api/generated";
import { odata } from "@/api/odata";
import InvoiceEditor from "@/components/InvoiceEditor.vue";
import InvoiceRowActions from "@/components/InvoiceRowActions.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import { useDebounced } from "@/composables/useDebounced";
import { usePageReport } from "@/composables/usePageReport";
import { formatDate, formatInvoiceNumber, formatRevisionBadge } from "@/lib/format";
import { lastPageStart, PAGE_SIZE, PAGINATOR_TEMPLATE } from "@/lib/paging";

const { t } = useI18n();
const search = ref("");
const term = useDebounced(search);
const payment = ref<"all" | "unpaid" | "paid">("all");
const first = ref(0);

// a new search or filter starts on page one
watch([term, payment], () => {
  first.value = 0;
});

const paymentOptions = computed(() => [
  { value: "all", label: t("paymentFilterAll") },
  { value: "unpaid", label: t("paymentFilterUnpaid") },
  { value: "paid", label: t("paymentFilterPaid") },
]);

const invoices = useQuery({
  queryKey: ["invoices", "list", term, payment, first],
  queryFn: async () => {
    const searchTerm = term.value.trim();
    const response = await odata
      .Invoices()
      .query((b, q) => {
        // ordinary invoices only; grave invoices are reached through their grave
        let filter = q.GraveId.isNull().and(q.IsLatestRevision.isTrue());
        if (payment.value === "unpaid") {
          filter = filter.and(q.PaidOn.isNull());
        } else if (payment.value === "paid") {
          filter = filter.and(q.PaidOn.isNotNull());
        }
        b.filter(filter).orderBy(q.Id.desc()).skip(first.value).top(PAGE_SIZE).count();
      })
      // `search` is the backend's own query option: number, recipient, date, total
      .execute(searchTerm ? { params: { search: searchTerm } } : undefined);
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
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <IconField class="w-full md:w-80">
          <InputIcon class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('searchInvoice')"
            :aria-label="t('searchInvoice')"
            class="w-full"
          />
        </IconField>
        <Select
          v-model="payment"
          :options="paymentOptions"
          option-label="label"
          option-value="value"
          :aria-label="t('paymentFilter')"
          class="w-full sm:w-56"
        />
      </div>

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
        @page="first = $event.first"
      >
        <template #empty>{{ t("noData") }}</template>
        <Column :header="t('invoiceNumber')">
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
        </Column>
        <Column
          field="RecipientName"
          :header="t('recipientName')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell"
        />
        <Column
          :header="t('invoiceDate')"
          header-class="hidden md:table-cell"
          body-class="hidden md:table-cell tabular-nums"
        >
          <template #body="{ data }">{{ formatDate(data.GeneratedAt) }}</template>
        </Column>
        <Column
          :header="t('paidStatus')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell"
        >
          <template #body="{ data }"><PaidStatusTag :paid-on="data.PaidOn" /></template>
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
