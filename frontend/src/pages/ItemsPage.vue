<script setup lang="ts">
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import type { InvoiceItem } from "@/api/generated";
import { useItemsQuery, useVatRatesQuery } from "@/api/queries";
import ColumnFilterCell from "@/components/ColumnFilterCell.vue";
import ColumnFilterSheet from "@/components/ColumnFilterSheet.vue";
import ItemDialog from "@/components/ItemDialog.vue";
import PageHeader from "@/components/PageHeader.vue";
import { type ColumnFilterField, useColumnFilters } from "@/composables/useColumnFilters";
import { formatAmount, formatVatRate } from "@/lib/format";
import { QUARTERS, quarterLabelKey } from "@/lib/quarter";
import { matchesAmount, matchesWords } from "@/lib/searchWords";

const { t } = useI18n();
const filters = useColumnFilters({
  number: "",
  description: "",
  price: "",
  vat: "",
  quarter: "",
});
// the whole catalogue is read once and filtered here
const items = useItemsQuery(ref(""));
const vatRates = useVatRatesQuery();

const vatOptions = computed(() =>
  (vatRates.data.value ?? []).map((rate) => ({
    value: String(rate.Rate),
    label: `${formatVatRate(rate.Rate)} %`,
  })),
);

const quarterOptions = computed(() => [
  { value: "none", label: t("quarterNone") },
  ...QUARTERS.map((quarter) => ({ value: quarter, label: t(quarterLabelKey(quarter)) })),
]);

// the sheet lists every searchable column, the ones a narrow table hides too
const fields = computed<ColumnFilterField[]>(() => [
  { key: "number", header: t("itemNumber"), kind: "text" },
  { key: "description", header: t("itemDescription"), kind: "text" },
  { key: "vat", header: t("vatRate"), kind: "select", options: vatOptions.value },
  { key: "quarter", header: t("itemQuarter"), kind: "select", options: quarterOptions.value },
  { key: "price", header: t("itemDefaultPrice"), kind: "text" },
]);

function field(key: string): ColumnFilterField {
  return fields.value.find((each) => each.key === key)!;
}

const rows = computed(() => {
  const applied = filters.applied.value;
  // a select value that is no option counts as "Alle"
  const vat = vatOptions.value.find((option) => option.value === applied.vat)?.value;
  const quarter = quarterOptions.value.find((option) => option.value === applied.quarter)?.value;
  return (items.data.value ?? []).filter(
    (item) =>
      matchesWords(applied.number, item.Number) &&
      matchesWords(applied.description, item.Description) &&
      matchesAmount(applied.price, item.DefaultPrice) &&
      (vat === undefined || String(item.VatRate) === vat) &&
      (quarter === undefined || String(item.Quarter ?? "none") === quarter),
  );
});

const dialogOpen = ref(false);
const editing = ref<InvoiceItem | null>(null);

function open(item: InvoiceItem | null): void {
  editing.value = item;
  dialogOpen.value = true;
}
</script>

<template>
  <PageHeader :title="t('menuItems')">
    <template #actions>
      <Button :label="t('createItem')" icon="pi pi-plus" @click="open(null)" />
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
        :value="rows"
        data-key="Id"
        :loading="items.isFetching.value"
        striped-rows
        filter-display="row"
        :filters="{}"
        class="column-filters-lg"
      >
        <template #empty>{{ t("noData") }}</template>
        <!-- a number has no space to break at; without this a long one widens
             its column until the row actions leave the card on a small screen -->
        <Column
          field="Number"
          :header="t('itemNumber')"
          body-class="[overflow-wrap:anywhere]"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #filter>
            <ColumnFilterCell v-model="filters.values.number" :field="field('number')" />
          </template>
        </Column>
        <Column
          field="Description"
          :header="t('itemDescription')"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #filter>
            <ColumnFilterCell v-model="filters.values.description" :field="field('description')" />
          </template>
        </Column>
        <Column
          :header="t('vatRate')"
          header-class="hidden whitespace-nowrap md:table-cell"
          body-class="hidden md:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ formatVatRate(data.VatRate) }} %</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.vat" :field="field('vat')" />
          </template>
        </Column>
        <Column
          :header="t('itemQuarter')"
          header-class="hidden lg:table-cell"
          body-class="hidden lg:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ t(quarterLabelKey(data.Quarter)) }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.quarter" :field="field('quarter')" />
          </template>
        </Column>
        <Column
          :header="t('itemDefaultPrice')"
          header-class="hidden sm:table-cell col-num"
          body-class="hidden text-right tabular-nums sm:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ formatAmount(data.DefaultPrice) }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.price" :field="field('price')" />
          </template>
        </Column>
        <Column class="w-16 text-right">
          <template #body="{ data }">
            <Button
              v-tooltip.left="t('edit')"
              icon="pi pi-pencil"
              text
              rounded
              :aria-label="`${t('edit')}: ${data.Number}`"
              @click="open(data)"
            />
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <ItemDialog v-model:visible="dialogOpen" :item="editing" />
</template>
