<script setup lang="ts">
import { useQuery, useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import DatePicker from "primevue/datepicker";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import SelectButton from "primevue/selectbutton";
import Tag from "primevue/tag";
import { computed, onMounted, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink, useRouter } from "vue-router";
import {
  bulkDownload,
  fetchGraveStatus,
  generateGraveInvoices,
  type GraveStatusRow,
  pollJob,
  searchGraves,
} from "@/api/endpoints";
import { ApiError, isTransient } from "@/api/http";
import { useGraveYearRange } from "@/api/queries";
import { type Job, JobStatus } from "@/api/generated";
import GenerateDialog from "@/components/GenerateDialog.vue";
import GraveEditor from "@/components/GraveEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import { useBlocking } from "@/composables/useBlocking";
import { useDebounced } from "@/composables/useDebounced";
import { useErrorToast } from "@/composables/useErrorToast";
import { showMessage } from "@/composables/useMessage";
import { downloadBlob } from "@/lib/download";
import { forgetJob, rememberedJob, rememberJob } from "@/lib/followedJob";
import { formatMonth, type Translate } from "@/lib/format";
import { generationSummary } from "@/lib/generation";
import { currentHalfYear, halfYearKey, halfYearText, parseHalfYearKey } from "@/lib/halfYear";

const { t } = useI18n();
const translate: Translate = (key, list) => (list ? t(key, list) : t(key));
const router = useRouter();
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();

const [currentYear, currentHalf] = currentHalfYear();
const halfYear = ref(halfYearKey(currentYear, currentHalf));
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
    halfYear.value = halfYearKey(value.getFullYear(), period.value[1]);
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
    halfYear.value = halfYearKey(period.value[0], value);
  },
});
const payment = ref<"all" | "unpaid">("all");
const paymentOptions = computed(() => [
  { value: "all", label: t("paymentFilterAll") },
  { value: "unpaid", label: t("paymentFilterUnpaid") },
]);

const search = ref("");
const term = useDebounced(search);

// not-yet-invoiced graves first -- the server's order
const status = useQuery({
  queryKey: ["graves", "status", halfYear],
  queryFn: () => fetchGraveStatus(period.value[0], period.value[1]),
});
const searchResult = useQuery({
  queryKey: ["graves", "search", term],
  queryFn: () => searchGraves(term.value.trim()),
  enabled: computed(() => term.value.trim() !== ""),
});

const rows = computed(() => {
  let list = status.data.value ?? [];
  if (term.value.trim()) {
    const ids = new Set((searchResult.data.value ?? []).map((result) => result.Id));
    list = list.filter((row) => ids.has(row.Id));
  }
  if (payment.value === "unpaid") {
    list = list.filter((row) => row.InvoiceCount > 0 && row.PaidCount < row.InvoiceCount);
  }
  return list;
});

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
function openEditor(graveId: number | null): void {
  editorGraveId.value = graveId;
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
        <IconField class="w-full md:w-[26rem]">
          <InputIcon class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('searchGrave')"
            :aria-label="t('searchGrave')"
            class="w-full"
          />
        </IconField>
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
        <Select
          v-model="payment"
          :options="paymentOptions"
          option-label="label"
          option-value="value"
          :aria-label="t('paymentFilter')"
          class="w-full sm:w-56"
        />
      </div>

      <!-- the search narrows the status list, so its own fetch is loading too -->
      <DataTable
        :value="rows"
        data-key="Id"
        :loading="status.isFetching.value || searchResult.isFetching.value"
        striped-rows
      >
        <template #empty>{{ t("noData") }}</template>
        <Column :header="t('graveNumber')">
          <template #body="{ data }">
            <!-- may break anywhere: a grave number has no space to break at, and
                 a long one would otherwise push the row actions off the card -->
            <RouterLink
              :to="`/graves/${data.Id}`"
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
          :header="t('graveFieldNumber')"
          header-class="hidden lg:table-cell"
          body-class="hidden lg:table-cell"
        >
          <template #body="{ data }">{{ data.FieldNumber || "–" }}</template>
        </Column>
        <Column
          :header="t('startDate')"
          header-class="hidden xl:table-cell"
          body-class="hidden xl:table-cell tabular-nums"
        >
          <template #body="{ data }">{{ formatMonth(data.StartDate) }}</template>
        </Column>
        <Column
          :header="t('endDate')"
          header-class="hidden xl:table-cell"
          body-class="hidden xl:table-cell tabular-nums"
        >
          <template #body="{ data }">{{ formatMonth(data.EndDate) || "–" }}</template>
        </Column>
        <Column :header="t('invoicingStatus')">
          <template #body="{ data }">
            <Tag
              :value="data.Invoiced ? t('statusInvoiced') : t('statusOpen')"
              :severity="data.Invoiced ? 'success' : 'danger'"
              :icon="data.Invoiced ? 'pi pi-check' : 'pi pi-exclamation-circle'"
              role="img"
              :aria-label="
                data.Invoiced ? t('statusInvoicedAnnouncement') : t('statusOpenAnnouncement')
              "
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
        </Column>
        <Column
          :header="t('paidStatus')"
          header-class="hidden sm:table-cell"
          body-class="hidden sm:table-cell"
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
  <GraveEditor v-model:visible="editorOpen" :grave-id="editorGraveId" />
</template>
