<script setup lang="ts">
import { useQuery, useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import DatePicker from "primevue/datepicker";
import SelectButton from "primevue/selectbutton";
import Tag from "primevue/tag";
import { computed, nextTick, onMounted, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink, useRouter } from "vue-router";
import {
  bulkDownload,
  fetchGraveStatus,
  generateGraveInvoices,
  type GraveDetails,
  type GraveStatusRow,
  pollJob,
} from "@/api/endpoints";
import { ApiError, isTransient } from "@/api/http";
import { useCemeteriesQuery, useGraveYearRange } from "@/api/queries";
import { type Job, JobStatus } from "@/api/generated";
import ColumnFilterCell from "@/components/ColumnFilterCell.vue";
import ColumnFilterSheet from "@/components/ColumnFilterSheet.vue";
import GenerateDialog from "@/components/GenerateDialog.vue";
import GraveEditor from "@/components/GraveEditor.vue";
import GraveRecipientsCell from "@/components/GraveRecipientsCell.vue";
import PageHeader from "@/components/PageHeader.vue";
import { useBlocking } from "@/composables/useBlocking";
import { type ColumnFilterField, useColumnFilters } from "@/composables/useColumnFilters";
import { useErrorToast } from "@/composables/useErrorToast";
import { showMessage } from "@/composables/useMessage";
import { usePageReport } from "@/composables/usePageReport";
import { downloadBlob } from "@/lib/download";
import { forgetJob, rememberedJob, rememberJob } from "@/lib/followedJob";
import { formatMonth, type Translate } from "@/lib/format";
import { generationSummary } from "@/lib/generation";
import { currentHalfYear, halfYearKey, halfYearText, parseHalfYearKey } from "@/lib/halfYear";
import { lastPageStart, PAGE_SIZE, PAGINATOR_TEMPLATE } from "@/lib/paging";
import { matchesWords } from "@/lib/searchWords";

const { t } = useI18n();
const translate: Translate = (key, list) => (list ? t(key, list) : t(key));
const router = useRouter();
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();

const [currentYear, currentHalf] = currentHalfYear();
const todaysHalfYear = halfYearKey(currentYear, currentHalf);

// The half-year shown goes into the URL with the column filters, so coming
// back from a grave shows the list that was left. It is no column of its own:
// the year field and the H1/H2 buttons set it, it has no chip, and clearing
// the filters leaves it alone.
const filters = useColumnFilters({
  period: todaysHalfYear,
  number: "",
  site: "",
  cemetery: "",
  field: "",
  start: "",
  end: "",
  recipients: "",
  status: "",
  paid: "",
});
function clearColumns(): void {
  for (const key of Object.keys(filters.values) as (keyof typeof filters.values)[]) {
    if (key !== "period") {
      filters.clear(key);
    }
  }
}

// a URL whose period is no half-year shows today's
const halfYear = computed(() =>
  /^\d{4}-[12]$/.test(filters.values.period) ? filters.values.period : todaysHalfYear,
);
const period = computed(() => parseHalfYearKey(halfYear.value));
const halves = [
  { value: 1, label: "H1" },
  { value: 2, label: "H2" },
];

// the years that have graves, and next year: graves without an end run on
const yearRange = useGraveYearRange();
// a picker must not open on a disabled year when every grave starts in the future
const minYearDate = computed(
  () => new Date(Math.min(yearRange.data.value?.first ?? currentYear - 2, currentYear), 0, 1),
);
const maxYearDate = computed(() => new Date(yearRange.data.value?.last ?? currentYear + 1, 11, 31));
// the field's own value: empty while the user retypes it, whereas the kept
// year (in `halfYear`) is what the page queries with
const yearField = ref<Date | null>(new Date(period.value[0], 0, 1));
watch(period, ([year]) => {
  if (yearField.value?.getFullYear() !== year) {
    yearField.value = new Date(year, 0, 1);
  }
});
function setYear(value: Date | Date[] | (Date | null)[] | null | undefined): void {
  if (value instanceof Date) {
    filters.values.period = halfYearKey(value.getFullYear(), period.value[1]);
  }
}
// a field left empty shows the kept year again
function restoreYear(): void {
  if (yearField.value == null) {
    yearField.value = new Date(period.value[0], 0, 1);
  }
}
const half = computed({
  get: () => period.value[1],
  set: (value: number) => {
    filters.values.period = halfYearKey(period.value[0], value);
  },
});

const cemeteries = useCemeteriesQuery();
const cemeteryOptions = computed(() =>
  (cemeteries.data.value ?? []).map((cemetery) => ({
    value: String(cemetery.Id),
    label: cemetery.Name,
  })),
);
const statusOptions = computed(() => [
  { value: "open", label: t("statusOpen") },
  { value: "invoiced", label: t("statusInvoiced") },
]);
const paidOptions = computed(() => [{ value: "unpaid", label: t("paymentFilterUnpaid") }]);

// the sheet lists every searchable column, the ones a narrow table hides too
const fields = computed<ColumnFilterField[]>(() => [
  { key: "number", header: t("graveNumber"), kind: "text" },
  { key: "site", header: t("graveSiteName"), kind: "text" },
  { key: "cemetery", header: t("graveCemetery"), kind: "select", options: cemeteryOptions.value },
  { key: "field", header: t("graveFieldNumber"), kind: "text" },
  { key: "start", header: t("startDate"), kind: "text" },
  { key: "end", header: t("endDate"), kind: "text" },
  { key: "recipients", header: t("graveRecipients"), kind: "text" },
  { key: "status", header: t("invoicingStatus"), kind: "select", options: statusOptions.value },
  { key: "paid", header: t("paidStatus"), kind: "select", options: paidOptions.value },
]);

function field(key: string): ColumnFilterField {
  return fields.value.find((each) => each.key === key)!;
}

// not-yet-invoiced graves first -- the server's order
const status = useQuery({
  queryKey: ["graves", "status", halfYear],
  queryFn: () => fetchGraveStatus(period.value[0], period.value[1]),
});

// the value the status column shows and the status filter selects
function statusOf(row: GraveStatusRow): "invoiced" | "open" {
  return row.Invoiced ? "invoiced" : "open";
}
const statusTags = computed(() => ({
  invoiced: {
    value: t("statusInvoiced"),
    severity: "success" as const,
    icon: "pi pi-check",
    label: t("statusInvoicedAnnouncement"),
  },
  open: {
    value: t("statusOpen"),
    severity: "danger" as const,
    icon: "pi pi-exclamation-circle",
    label: t("statusOpenAnnouncement"),
  },
}));

const rows = computed(() => {
  const applied = filters.applied.value;
  // a select value that is no option counts as "Alle"
  const option = (options: { value: string }[], value: string) =>
    options.find((each) => each.value === value)?.value;
  const cemetery = option(cemeteryOptions.value, applied.cemetery);
  const invoicing = option(statusOptions.value, applied.status);
  const paid = option(paidOptions.value, applied.paid);
  return (status.data.value ?? []).filter(
    (row) =>
      matchesWords(applied.number, row.GraveNumber) &&
      matchesWords(applied.site, row.SiteName) &&
      matchesWords(applied.field, row.FieldNumber) &&
      matchesWords(applied.start, formatMonth(row.StartDate)) &&
      matchesWords(applied.end, row.EndDate == null ? null : formatMonth(row.EndDate)) &&
      matchesWords(
        applied.recipients,
        ...row.Recipients.flatMap((recipient) => [recipient.Name, recipient.Address]),
      ) &&
      (cemetery === undefined || String(row.CemeteryId) === cemetery) &&
      (invoicing === undefined || statusOf(row) === invoicing) &&
      (paid === undefined || (row.InvoiceCount > 0 && row.PaidCount < row.InvoiceCount)),
  );
});

// The half-year's list arrives whole and is searched and filtered here, so the
// pages are cut from what is left of it.
const first = ref(0);
// a new search, filter or half-year starts on page one; the half-year at once,
// since its list is fetched at once too
watch([filters.applied, halfYear], () => {
  first.value = 0;
});
// a page that emptied -- its last unpaid grave marked paid under "unpaid
// only" -- moves back to the last page that has rows
watch(
  () => rows.value.length,
  (total) => {
    if (first.value >= total) {
      first.value = lastPageStart(total);
    }
  },
);
const pageReport = usePageReport();

function paymentTag(
  row: GraveStatusRow,
): { value: string; severity: "success" | "warn"; label: string } | null {
  if (row.InvoiceCount === 0) {
    return null;
  }
  if (row.PaidCount === row.InvoiceCount) {
    return { value: t("statusPaid"), severity: "success", label: t("paymentPaidAnnouncement") };
  }
  if (row.PaidCount === 0) {
    return { value: t("statusUnpaid"), severity: "warn", label: t("paymentUnpaidAnnouncement") };
  }
  return {
    value: t("statusPartlyPaid", [row.PaidCount, row.InvoiceCount]),
    severity: "warn",
    label: t("paymentPartlyPaidAnnouncement"),
  };
}

const zipping = ref(false);
async function downloadZip(): Promise<void> {
  // the server answers an empty archive, which was saved without a word
  if (status.data.value && !status.data.value.some((row) => row.InvoiceCount > 0)) {
    showMessage(
      t("bulkGraveInvoices"),
      t("bulkNothingToDownload", [halfYearText(period.value[0], period.value[1])]),
      "info",
    );
    return;
  }
  zipping.value = true;
  try {
    downloadBlob(await bulkDownload("Normal", period.value[0], period.value[1]), "rechnungen.zip");
  } catch (error) {
    showError(error);
  } finally {
    zipping.value = false;
  }
}

const generateOpen = ref(false);
async function generate(year: number, half: number): Promise<void> {
  generateOpen.value = false;
  await follow(async () => (await generateGraveInvoices(year, half)).Id);
}

/** Waits for a generation behind the blocking overlay and reports its result.
 *  `start` answers the job's id: a new job's, or that of one this browser
 *  started before the page was loaded anew. */
async function follow(start: () => Promise<number>): Promise<void> {
  let started = false;
  let job: Job | undefined;
  try {
    job = await run(t("generationRunning"), async (progress) => {
      const jobId = await start();
      rememberJob(jobId);
      started = true;
      return pollJob(jobId, (percent) => progress(percent, t("generationProgress", [percent])));
    });
  } catch (error) {
    if (started && isTransient(error)) {
      // Lost from view, not failed: the job runs on in the server, and the
      // page picks it up again once it is loaded anew.
      showError(new ApiError("GenerationUnfollowed"));
    } else {
      forgetJob();
      showError(error);
    }
  } finally {
    // however the wait ended, some graves may have been invoiced meanwhile
    await queryClient.invalidateQueries();
  }
  if (!job) {
    return;
  }
  forgetJob();
  if (job.Status === JobStatus.Failed) {
    showMessage(t("generateInvoices"), t("generationFailed"));
    return;
  }
  showMessage(t("generateInvoices"), generationSummary(job.ResultJson, translate), "info");
}

// A generation this browser started and has not shown the result of: still
// running, it blocks the page loaded anew like the one that started it; done
// meanwhile, its result -- the one place skipped graves are named -- is shown
// now rather than never.
onMounted(async () => {
  const jobId = rememberedJob();
  if (jobId !== undefined) {
    await follow(() => Promise.resolve(jobId));
  }
});

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
  <PageHeader :title="t('menuGraves')">
    <template #actions>
      <Button
        :label="t('bulkGraveInvoices')"
        icon="pi pi-download"
        severity="secondary"
        outlined
        :loading="zipping"
        @click="downloadZip"
      />
      <Button
        :label="t('generateInvoices')"
        icon="pi pi-bolt"
        severity="secondary"
        outlined
        @click="generateOpen = true"
      />
      <Button :label="t('createGrave')" icon="pi pi-plus" @click="openEditor(null)" />
    </template>
  </PageHeader>

  <Card>
    <template #content>
      <div class="mb-4 flex flex-wrap items-center gap-3">
        <DatePicker
          v-model="yearField"
          input-id="gravesYear"
          view="year"
          date-format="yy"
          :min-date="minYearDate"
          :max-date="maxYearDate"
          :aria-label="t('year')"
          show-icon
          class="w-full sm:w-36"
          @update:model-value="setYear"
          @hide="restoreYear"
        />
        <SelectButton
          v-model="half"
          :options="halves"
          option-label="label"
          option-value="value"
          :allow-empty="false"
          :aria-label="t('halfYear')"
        />
      </div>

      <ColumnFilterSheet
        v-model:values="filters.values"
        :fields="fields"
        breakpoint="2xl"
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
        :loading="status.isFetching.value"
        striped-rows
        filter-display="row"
        :filters="{}"
        class="column-filters-2xl compact-cells"
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
          header-class="hidden 2xl:table-cell"
          body-class="hidden 2xl:table-cell tabular-nums"
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
          header-class="hidden 2xl:table-cell"
          body-class="hidden 2xl:table-cell tabular-nums"
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
        <Column :header="t('invoicingStatus')" :show-filter-menu="false" :show-clear-button="false">
          <template #body="{ data }">
            <Tag
              :value="statusTags[statusOf(data)].value"
              :severity="statusTags[statusOf(data)].severity"
              :icon="statusTags[statusOf(data)].icon"
              role="img"
              :aria-label="statusTags[statusOf(data)].label"
            />
            <!-- on a phone the payment state sits under the status instead of in its own column -->
            <div v-if="paymentTag(data)" class="mt-1 sm:hidden">
              <Tag
                :value="paymentTag(data)?.value"
                :severity="paymentTag(data)?.severity"
                role="img"
                :aria-label="paymentTag(data)?.label"
              />
            </div>
          </template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.status" :field="field('status')" />
          </template>
        </Column>
        <Column
          :header="t('paidStatus')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell"
          :show-filter-menu="false"
          :show-clear-button="false"
        >
          <template #body="{ data }">
            <Tag
              v-if="paymentTag(data)"
              :value="paymentTag(data)?.value"
              :severity="paymentTag(data)?.severity"
              role="img"
              :aria-label="paymentTag(data)?.label"
            />
          </template>
          <template #filter>
            <ColumnFilterCell v-model="filters.values.paid" :field="field('paid')" />
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

  <GenerateDialog
    v-model:visible="generateOpen"
    :initial-year="period[0]"
    :initial-half="period[1]"
    @generate="generate"
  />
  <GraveEditor
    v-model:visible="editorOpen"
    :grave-id="editorGraveId"
    :successor-of="successorOf"
    :successor-start="successorStart"
    @hand-over="openSuccessor"
  />
</template>
