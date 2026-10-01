<script setup lang="ts">
import { useQuery, useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import IconField from "primevue/iconfield";
import InputIcon from "primevue/inputicon";
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import Tag from "primevue/tag";
import { computed, ref } from "vue";
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
import { type Job, JobStatus } from "@/api/generated";
import GenerateDialog from "@/components/GenerateDialog.vue";
import GraveEditor from "@/components/GraveEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import { useBlocking } from "@/composables/useBlocking";
import { useDebounced } from "@/composables/useDebounced";
import { useErrorToast } from "@/composables/useErrorToast";
import { showMessage } from "@/composables/useMessage";
import { downloadBlob } from "@/lib/download";
import { formatDate, type Translate } from "@/lib/format";
import { generationSummary } from "@/lib/generation";
import { currentHalfYear, halfYearKey, halfYearOptions, parseHalfYearKey } from "@/lib/halfYear";

const { t } = useI18n();
const translate: Translate = (key, list) => (list ? t(key, list) : t(key));
const router = useRouter();
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();

const [currentYear, currentHalf] = currentHalfYear();
const halfYear = ref(halfYearKey(currentYear, currentHalf));
const period = computed(() => parseHalfYearKey(halfYear.value));
const halfYearChoices = halfYearOptions(2, 1);
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
  let job: Job;
  try {
    job = await run(t("generationRunning"), async (progress) => {
      const started = await generateGraveInvoices(year, half);
      return pollJob(started.Id, (percent) =>
        progress(percent, t("generationProgress", [percent])),
      );
    });
  } catch (error) {
    showError(error);
    return;
  }
  await queryClient.invalidateQueries();
  if (job.Status === JobStatus.Failed) {
    showMessage(t("generateInvoices"), t("generationFailed"));
    return;
  }
  showMessage(t("generateInvoices"), generationSummary(job.ResultJson, translate), "info");
}

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
        <IconField class="w-full md:w-96">
          <InputIcon class="pi pi-search" />
          <InputText
            v-model="search"
            :placeholder="t('searchGrave')"
            :aria-label="t('searchGrave')"
            class="w-full"
          />
        </IconField>
        <Select
          v-model="halfYear"
          :options="halfYearChoices"
          option-label="text"
          option-value="key"
          :aria-label="t('halfYear')"
          class="w-full sm:w-40"
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
            <RouterLink
              :to="`/graves/${data.Id}`"
              class="font-medium whitespace-nowrap text-primary hover:underline"
            >
              {{ data.GraveNumber }}
            </RouterLink>
          </template>
        </Column>
        <Column
          :header="t('startDate')"
          header-class="hidden xl:table-cell"
          body-class="hidden xl:table-cell tabular-nums"
        >
          <template #body="{ data }">{{ formatDate(data.StartDate) }}</template>
        </Column>
        <Column
          :header="t('endDate')"
          header-class="hidden xl:table-cell"
          body-class="hidden xl:table-cell tabular-nums"
        >
          <template #body="{ data }">{{ formatDate(data.EndDate) }}</template>
        </Column>
        <Column :header="t('invoicingStatus')">
          <template #body="{ data }">
            <Tag
              :value="data.Invoiced ? t('statusInvoiced') : t('statusOpen')"
              :severity="data.Invoiced ? 'success' : 'danger'"
              :icon="data.Invoiced ? 'pi pi-check' : 'pi pi-exclamation-circle'"
              :aria-label="
                data.Invoiced ? t('statusInvoicedAnnouncement') : t('statusOpenAnnouncement')
              "
            />
            <!-- on a phone the payment state sits under the status instead of in its own column -->
            <div v-if="paymentTag(data)" class="mt-1 sm:hidden">
              <Tag
                :value="paymentTag(data)?.value"
                :severity="paymentTag(data)?.severity"
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
                :aria-label="t('actionDetails')"
                @click="router.push(`/graves/${data.Id}`)"
              />
              <Button
                v-tooltip.top="t('edit')"
                icon="pi pi-pencil"
                text
                rounded
                :aria-label="t('edit')"
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
