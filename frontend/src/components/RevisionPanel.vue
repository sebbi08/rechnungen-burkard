<script setup lang="ts">
import Button from "primevue/button";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import { useI18n } from "vue-i18n";
import { type DocumentKindName, quotePdfUrl } from "@/api/endpoints";
import { useErrorToast } from "@/composables/useErrorToast";
import { openDocument } from "@/lib/download";
import { formatAmount, formatVatRate } from "@/lib/format";
import { printedUnitPrice } from "@/lib/money";
import type { RevisionView } from "@/lib/revisions";

const props = defineProps<{ revision: RevisionView; kind: "invoice" | "quote" }>();
const { t } = useI18n();
const { showError } = useErrorToast();

function open(kind: DocumentKindName): void {
  openDocument(props.revision.Id, kind).catch(showError);
}

/** A quote has one PDF, no copy. */
function openQuote(): void {
  // opened inside the click, or the browser blocks it as a popup
  window.open(quotePdfUrl(props.revision.Id), "_blank");
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <div v-if="kind === 'quote'" class="flex flex-wrap gap-2">
      <Button
        :label="t('actionDownloadQuote')"
        icon="pi pi-file-pdf"
        size="small"
        outlined
        @click="openQuote"
      />
    </div>
    <div v-else class="flex flex-wrap gap-2">
      <Button
        :label="t('actionDownloadNormal')"
        icon="pi pi-file-pdf"
        size="small"
        outlined
        @click="open('Normal')"
      />
      <Button
        :label="t('actionDownloadCopy')"
        icon="pi pi-copy"
        size="small"
        outlined
        @click="open('Copy')"
      />
    </div>

    <DataTable :value="revision.Lines" size="small" class="min-w-0 overflow-x-auto">
      <Column field="ItemNumber" :header="t('itemNumber')" />
      <Column
        field="Description"
        :header="t('itemDescription')"
        header-class="hidden sm:table-cell"
        body-class="hidden sm:table-cell"
      />
      <Column
        field="Count"
        :header="t('itemCount')"
        header-class="col-num"
        body-class="text-right tabular-nums"
      />
      <Column
        :header="t('itemPrice')"
        header-class="hidden sm:table-cell col-num"
        body-class="hidden sm:table-cell text-right tabular-nums whitespace-nowrap"
      >
        <template #body="{ data }">{{ formatAmount(printedUnitPrice(data)) }}</template>
      </Column>
      <Column
        :header="t('netAmount')"
        header-class="col-num"
        body-class="text-right tabular-nums whitespace-nowrap"
      >
        <template #body="{ data }">{{ formatAmount(data.LineTotal) }}</template>
      </Column>
      <Column
        :header="t('grossAmount')"
        header-class="col-num"
        body-class="text-right tabular-nums whitespace-nowrap"
      >
        <template #body="{ data }">{{ formatAmount(data.GrossTotal) }}</template>
      </Column>
      <Column
        :header="t('vatRate')"
        header-class="hidden xl:table-cell col-num"
        body-class="hidden xl:table-cell text-right"
      >
        <template #body="{ data }">{{ formatVatRate(data.VatRate) }} %</template>
      </Column>
      <Column
        :header="t('priceIncludesVatColumn')"
        header-class="hidden xl:table-cell"
        body-class="hidden xl:table-cell"
      >
        <template #body="{ data }">{{ data.PriceIncludesVat ? t("yes") : t("no") }}</template>
      </Column>
    </DataTable>

    <dl class="m-0 ml-auto grid w-full max-w-xs grid-cols-[1fr_auto] gap-x-6 gap-y-1 tabular-nums">
      <dt class="text-muted-color">{{ t("netTotal") }}</dt>
      <dd class="m-0 text-right">{{ formatAmount(revision.NetTotal) }}</dd>
      <dt class="text-muted-color">{{ t("vatTotal") }}</dt>
      <dd class="m-0 text-right">{{ formatAmount(revision.VatTotal) }}</dd>
      <dt class="font-semibold">{{ t("grossTotal") }}</dt>
      <dd class="m-0 text-right font-semibold">{{ formatAmount(revision.GrossTotal) }}</dd>
    </dl>
  </div>
</template>
