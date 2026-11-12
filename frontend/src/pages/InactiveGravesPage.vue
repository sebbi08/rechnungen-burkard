<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import InputText from "primevue/inputtext";
import { computed, reactive, watch } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink } from "vue-router";
import { fetchInactiveGraves, type InactiveGraveRow } from "@/api/endpoints";
import PageHeader from "@/components/PageHeader.vue";
import { useShortTablePaging } from "@/composables/usePageReport";
import { formatDeletedDate, formatMonth } from "@/lib/format";
import { currentHalfYearStartMonth } from "@/lib/halfYear";
import { matchesWords } from "@/lib/searchWords";

const { t } = useI18n();

// ordered by grave number -- the server's order. Saving, deleting or restoring
// a grave invalidates every query, this one included.
const graves = useQuery({
  queryKey: ["graves", "inactive"],
  queryFn: fetchInactiveGraves,
});

interface Panel {
  key: string;
  title: string;
  cooperative: boolean;
  deleted: boolean;
}

const panels = computed<Panel[]>(() => [
  { key: "ended", title: t("panelEndedGraves"), cooperative: false, deleted: false },
  { key: "deleted", title: t("panelDeletedGraves"), cooperative: false, deleted: true },
  {
    key: "endedCooperative",
    title: t("panelEndedCooperativeGraves"),
    cooperative: true,
    deleted: false,
  },
  {
    key: "deletedCooperative",
    title: t("panelDeletedCooperativeGraves"),
    cooperative: true,
    deleted: true,
  },
]);

const terms = reactive<Record<string, string>>({
  ended: "",
  deleted: "",
  endedCooperative: "",
  deletedCooperative: "",
});

// A grave is in exactly one panel or none: deleted wins over ended, and one
// that has not ended (by the first month of the current half-year, as the
// other lists count it) and is not deleted is in none.
const halfStart = computed(currentHalfYearStartMonth);

function panelOf(row: InactiveGraveRow): string | null {
  const ended = row.EndDate != null && row.EndDate.slice(0, 7) < halfStart.value;
  if (row.DeletedAt == null && !ended) {
    return null;
  }
  return `${row.DeletedAt == null ? "ended" : "deleted"}${row.IsCooperative ? "Cooperative" : ""}`;
}

const sorted = computed(() => {
  const byPanel: Record<string, InactiveGraveRow[]> = {};
  for (const row of graves.data.value ?? []) {
    const key = panelOf(row);
    if (key != null) {
      (byPanel[key] ??= []).push(row);
    }
  }
  return byPanel;
});

function rowsOf(panel: Panel): InactiveGraveRow[] {
  const term = terms[panel.key] ?? "";
  return (sorted.value[panel.key] ?? []).filter((row) =>
    matchesWords(
      term,
      row.GraveNumber,
      row.SiteName,
      row.CemeteryName,
      row.ContractNumber,
      formatMonth(row.StartDate),
      row.EndDate == null ? null : formatMonth(row.EndDate),
      row.DeletedAt == null ? null : formatDeletedDate(row.DeletedAt),
    ),
  );
}

// Each panel pages what its own search leaves, and a changed search starts on
// page one. A panel that shrinks under its page, the paginator moves back itself.
const firsts = reactive<Record<string, number>>({
  ended: 0,
  deleted: 0,
  endedCooperative: 0,
  deletedCooperative: 0,
});
watch(
  () => ({ ...terms }),
  (now, before) => {
    for (const key of Object.keys(now)) {
      if (now[key] !== before[key]) {
        firsts[key] = 0;
      }
    }
  },
);
const paging = useShortTablePaging();
</script>

<template>
  <PageHeader :title="t('menuInactiveGraves')" />

  <div class="flex flex-col gap-6">
    <Card v-for="panel in panels" :key="panel.key" :data-testid="`panel-${panel.key}`">
      <template #title>
        <h2 class="m-0 text-xl font-semibold">{{ panel.title }}</h2>
      </template>
      <template #content>
        <InputText
          v-model="terms[panel.key]"
          class="mb-3 w-full sm:w-80"
          :aria-label="t('filterSearchColumn', [panel.title])"
          :placeholder="t('filterSearchColumn', [panel.title])"
        />
        <DataTable
          v-model:first="firsts[panel.key]"
          :value="rowsOf(panel)"
          data-key="Id"
          v-bind="paging"
          :loading="graves.isFetching.value"
          striped-rows
          class="compact-cells"
        >
          <template #empty>{{ t("noData") }}</template>
          <Column :header="t('graveNumber')">
            <template #body="{ data }">
              <!-- may break anywhere: a grave number has no space to break at -->
              <RouterLink
                :to="`/inactive-graves/${data.Id}`"
                class="font-medium [overflow-wrap:anywhere] text-primary hover:underline"
              >
                {{ data.GraveNumber }}
              </RouterLink>
            </template>
          </Column>
          <Column
            :header="t('graveSiteName')"
            header-class="hidden lg:table-cell"
            body-class="hidden lg:table-cell"
          >
            <template #body="{ data }">{{ data.SiteName || "–" }}</template>
          </Column>
          <Column
            :header="t('graveCemetery')"
            header-class="hidden lg:table-cell"
            body-class="hidden lg:table-cell"
          >
            <template #body="{ data }">{{ data.CemeteryName }}</template>
          </Column>
          <Column
            v-if="panel.cooperative"
            :header="t('contractNumber')"
            header-class="hidden lg:table-cell"
            body-class="hidden lg:table-cell"
          >
            <template #body="{ data }">{{ data.ContractNumber || "–" }}</template>
          </Column>
          <Column
            :header="t('startDate')"
            header-class="hidden md:table-cell"
            body-class="hidden md:table-cell tabular-nums"
          >
            <template #body="{ data }">{{ formatMonth(data.StartDate) }}</template>
          </Column>
          <Column :header="t('endDate')" body-class="tabular-nums">
            <template #body="{ data }">{{ formatMonth(data.EndDate) || "–" }}</template>
          </Column>
          <Column
            v-if="panel.deleted"
            :header="t('deletedOn')"
            header-class="hidden sm:table-cell"
            body-class="hidden sm:table-cell tabular-nums"
          >
            <template #body="{ data }">{{ formatDeletedDate(data.DeletedAt) }}</template>
          </Column>
        </DataTable>
      </template>
    </Card>
  </div>
</template>
