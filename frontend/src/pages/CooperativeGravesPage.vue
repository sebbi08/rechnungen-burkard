<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import Select from "primevue/select";
import { computed, nextTick, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink, useRouter } from "vue-router";
import { fetchCooperativeGraves, type GraveDetails } from "@/api/endpoints";
import { useCemeteriesQuery } from "@/api/queries";
import ColumnFilterCell from "@/components/ColumnFilterCell.vue";
import ColumnFilterSheet from "@/components/ColumnFilterSheet.vue";
import GraveEditor from "@/components/GraveEditor.vue";
import GraveRecipientsCell from "@/components/GraveRecipientsCell.vue";
import PageHeader from "@/components/PageHeader.vue";
import { type ColumnFilterField, useColumnFilters } from "@/composables/useColumnFilters";
import { usePageReport } from "@/composables/usePageReport";
import { formatMonth } from "@/lib/format";
import { lastPageStart, PAGE_SIZE, PAGINATOR_TEMPLATE } from "@/lib/paging";
import { matchesWords } from "@/lib/searchWords";

const { t } = useI18n();
const router = useRouter();

// The term shown goes into the URL with the column filters. It is no column of
// its own: it has no chip, and clearing the filters leaves it alone.
const filters = useColumnFilters({
  term: "active",
  number: "",
  site: "",
  cemetery: "",
  field: "",
  start: "",
  end: "",
  recipients: "",
});
function clearColumns(): void {
  for (const key of Object.keys(filters.values) as (keyof typeof filters.values)[]) {
    if (key !== "term") {
      filters.clear(key);
    }
  }
}

const termOptions = computed(() => [
  { value: "active", label: t("cooperativeTermActive") },
  { value: "ended", label: t("cooperativeTermEnded") },
  { value: "all", label: t("filterAll") },
]);
// a URL whose term is none of these shows the active graves
const term = computed(
  () => termOptions.value.find((each) => each.value === filters.values.term)?.value ?? "active",
);

const cemeteries = useCemeteriesQuery();
const cemeteryOptions = computed(() =>
  (cemeteries.data.value ?? []).map((cemetery) => ({
    value: String(cemetery.Id),
    label: cemetery.Name,
  })),
);

// the sheet lists every searchable column, the ones a narrow table hides too
const fields = computed<ColumnFilterField[]>(() => [
  { key: "number", header: t("graveNumber"), kind: "text" },
  { key: "site", header: t("graveSiteName"), kind: "text" },
  { key: "cemetery", header: t("graveCemetery"), kind: "select", options: cemeteryOptions.value },
  { key: "field", header: t("graveFieldNumber"), kind: "text" },
  { key: "start", header: t("startDate"), kind: "text" },
  { key: "end", header: t("endDate"), kind: "text" },
  { key: "recipients", header: t("graveRecipients"), kind: "text" },
]);

function field(key: string): ColumnFilterField {
  return fields.value.find((each) => each.key === key)!;
}

// ordered by grave number -- the server's order. Saving a grave invalidates
// every query, this one included.
const graves = useQuery({
  queryKey: ["graves", "cooperative"],
  queryFn: fetchCooperativeGraves,
});

const rows = computed(() => {
  const applied = filters.applied.value;
  const cemetery = cemeteryOptions.value.find((each) => each.value === applied.cemetery)?.value;
  const now = new Date();
  const thisMonth = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}`;
  return (graves.data.value ?? []).filter((row) => {
    // active: no end, or an end in this month or later
    const active = row.EndDate == null || row.EndDate.slice(0, 7) >= thisMonth;
    return (
      (term.value === "all" || (term.value === "active") === active) &&
      matchesWords(applied.number, row.GraveNumber) &&
      matchesWords(applied.site, row.SiteName) &&
      matchesWords(applied.field, row.FieldNumber) &&
      matchesWords(applied.start, formatMonth(row.StartDate)) &&
      matchesWords(applied.end, row.EndDate == null ? null : formatMonth(row.EndDate)) &&
      matchesWords(
        applied.recipients,
        ...row.Recipients.flatMap((recipient) => [recipient.Name, recipient.Address]),
      ) &&
      (cemetery === undefined || String(row.CemeteryId) === cemetery)
    );
  });
});

// the list arrives whole and is filtered here, so the pages are cut from what
// is left of it
const first = ref(0);
watch([filters.applied, term], () => {
  first.value = 0;
});
watch(
  () => rows.value.length,
  (total) => {
    if (first.value >= total) {
      first.value = lastPageStart(total);
    }
  },
);
const pageReport = usePageReport();

const editorOpen = ref(false);
const editorGraveId = ref<number | null>(null);
const successorOf = ref<GraveDetails | null>(null);
const successorStart = ref<Date | null>(null);
function openEditor(graveId: number | null): void {
  editorGraveId.value = graveId;
  successorOf.value = null;
  editorOpen.value = true;
}
// The editor has closed; the grave handed over takes its place as a new one.
async function openSuccessor(grave: GraveDetails, month: Date): Promise<void> {
  editorGraveId.value = null;
  successorOf.value = grave;
  successorStart.value = month;
  await nextTick();
  editorOpen.value = true;
}
</script>

<template>
  <PageHeader :title="t('menuCooperativeGraves')">
    <template #actions>
      <Button :label="t('createCooperativeGrave')" icon="pi pi-plus" @click="openEditor(null)" />
    </template>
  </PageHeader>

  <Card>
    <template #content>
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <Select
          v-model="filters.values.term"
          :options="termOptions"
          option-label="label"
          option-value="value"
          :aria-label="t('cooperativeTerm')"
          class="w-full sm:w-48"
        />
      </div>

      <ColumnFilterSheet
        v-model:values="filters.values"
        :fields="fields"
        breakpoint="xl"
        @clear="clearColumns"
      />

      <DataTable
        v-model:first="first"
        :value="rows"
        data-key="Id"
        paginator
        :rows="PAGE_SIZE"
        :paginator-template="PAGINATOR_TEMPLATE"
        :current-page-report-template="pageReport"
        :loading="graves.isFetching.value"
        striped-rows
        filter-display="row"
        :filters="{}"
        class="column-filters-xl compact-cells"
      >
        <template #empty>{{ t("noData") }}</template>
        <Column :header="t('graveNumber')" :show-filter-menu="false" :show-clear-button="false">
          <template #body="{ data }">
            <!-- may break anywhere: a grave number has no space to break at, and
                 a long one would otherwise push the row actions off the card -->
            <RouterLink
              :to="`/graves/${data.Id}`"
              class="font-medium [overflow-wrap:anywhere] text-primary hover:underline"
            >
              {{ data.GraveNumber }}
            </RouterLink>
            <!-- on a phone the recipients sit under the number instead of in their own column -->
            <div v-if="data.Recipients.length > 0" class="mt-1 sm:hidden">
              <GraveRecipientsCell :recipients="data.Recipients" />
            </div>
          </template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.number" :field="field('number')" />
          </template>
        </Column>
        <Column
          :header="t('graveSiteName')"
          header-class="hidden lg:table-cell"
          body-class="hidden lg:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ data.SiteName || "–" }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.site" :field="field('site')" />
          </template>
        </Column>
        <Column
          :header="t('graveCemetery')"
          header-class="hidden lg:table-cell"
          body-class="hidden lg:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ data.CemeteryName }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.cemetery" :field="field('cemetery')" />
          </template>
        </Column>
        <Column
          :header="t('graveFieldNumber')"
          header-class="hidden lg:table-cell"
          body-class="hidden lg:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ data.FieldNumber || "–" }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.field" :field="field('field')" />
          </template>
        </Column>
        <Column
          :header="t('startDate')"
          header-class="hidden xl:table-cell"
          body-class="hidden xl:table-cell tabular-nums"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ formatMonth(data.StartDate) }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.start" :field="field('start')" />
          </template>
        </Column>
        <Column
          :header="t('endDate')"
          header-class="hidden xl:table-cell"
          body-class="hidden xl:table-cell tabular-nums"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">{{ formatMonth(data.EndDate) || "–" }}</template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.end" :field="field('end')" />
          </template>
        </Column>
        <Column
          :header="t('graveRecipients')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">
            <GraveRecipientsCell :recipients="data.Recipients" />
          </template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.recipients" :field="field('recipients')" />
          </template>
        </Column>
        <Column class="w-0 whitespace-nowrap">
          <template #body="{ data }">
            <div class="flex justify-end">
              <!-- the grave number already links here; on a phone that is enough -->
              <Button
                v-tooltip.top="t('actionDetails')"
                class="hidden sm:inline-flex"
                icon="pi pi-eye"
                text
                rounded
                :aria-label="`${t('actionDetails')}: ${data.GraveNumber}`"
                @click="router.push(`/graves/${data.Id}`)"
              />
              <Button
                v-tooltip.top="t('edit')"
                icon="pi pi-pencil"
                text
                rounded
                :aria-label="`${t('edit')}: ${data.GraveNumber}`"
                @click="openEditor(data.Id)"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </template>
  </Card>

  <GraveEditor
    v-model:visible="editorOpen"
    :grave-id="editorGraveId"
    :successor-of="successorOf"
    :successor-start="successorStart"
    :initial-cooperative="true"
    @hand-over="openSuccessor"
  />
</template>
