<script setup lang="ts">
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import InputText from "primevue/inputtext";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import type { InvoiceItem } from "@/api/generated";
import { useItemsQuery } from "@/api/queries";
import ItemDialog from "@/components/ItemDialog.vue";
import PageHeader from "@/components/PageHeader.vue";
import { useDebounced } from "@/composables/useDebounced";
import { formatAmount, formatVatRate } from "@/lib/format";
import { quarterLabelKey } from "@/lib/quarter";

const { t } = useI18n();
const search = ref("");
const items = useItemsQuery(useDebounced(search));

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
      <div class="mb-4">
        <IconField class="w-full md:w-96">
          <InputIcon class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('searchByNumber')"
            :aria-label="t('searchByNumber')"
            class="w-full"
          />
        </IconField>
      </div>
      <DataTable
        :value="items.data.value ?? []"
        data-key="Id"
        :loading="items.isFetching.value"
        striped-rows
      >
        <template #empty>{{ t("noData") }}</template>
        <Column field="Number" :header="t('itemNumber')" />
        <Column field="Description" :header="t('itemDescription')" />
        <Column
          :header="t('vatRate')"
          header-class="hidden whitespace-nowrap md:table-cell"
          body-class="hidden md:table-cell"
        >
          <template #body="{ data }">{{ formatVatRate(data.VatRate) }} %</template>
        </Column>
        <Column
          :header="t('itemQuarter')"
          header-class="hidden md:table-cell"
          body-class="hidden md:table-cell"
        >
          <template #body="{ data }">{{ t(quarterLabelKey(data.Quarter)) }}</template>
        </Column>
        <Column
          :header="t('itemDefaultPrice')"
          header-class="hidden sm:table-cell col-num"
          body-class="hidden text-right tabular-nums sm:table-cell"
        >
          <template #body="{ data }">{{ formatAmount(data.DefaultPrice) }}</template>
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
