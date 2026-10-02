<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import {
  downloadImportTemplate,
  importArchive,
  type ImportProblem,
  ImportRejectedError,
} from "@/api/endpoints";
import { ApiError } from "@/api/http";
import PageHeader from "@/components/PageHeader.vue";
import { useBlocking } from "@/composables/useBlocking";
import { useErrorToast } from "@/composables/useErrorToast";
import { showMessage } from "@/composables/useMessage";
import { downloadBlob } from "@/lib/download";
import { formatAmount, formatVatRate } from "@/lib/format";

const i18n = useI18n();
const { t } = i18n;
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();

const fileInput = ref<HTMLInputElement>();
const file = ref<File | null>(null);
const problems = ref<ImportProblem[]>([]);
const downloading = ref(false);

function onFileChosen(event: Event): void {
  file.value = (event.target as HTMLInputElement).files?.[0] ?? null;
  problems.value = [];
}

/** Clears the input first: picking the same file again -- fixed after a
 *  rejection -- fires no change event otherwise, and the stale File went up. */
function chooseFile(): void {
  if (fileInput.value) {
    fileInput.value.value = "";
    fileInput.value.click();
  }
}

/** The problem in German, from its key and values; the backend's English
 *  sentence where the key is unknown. The rate and amounts of a split
 *  problem come as invariant numbers and are shown the German way. */
function problemText(problem: ImportProblem): string {
  const key = `importProblem${problem.messageKey ?? ""}`;
  if (!problem.messageKey || !i18n.te(key)) {
    return problem.message;
  }
  let args = problem.messageArgs ?? [];
  if (problem.messageKey.startsWith("PricesDoNotDivide")) {
    args = args.map((arg, index) =>
      index === 0 ? formatVatRate(Number(arg)) : index === 2 ? arg : formatAmount(Number(arg)),
    );
  } else if (problem.messageKey === "VatRateNotConfigured") {
    args = args.map((arg) => formatVatRate(Number(arg)));
  }
  return t(key, args);
}

async function downloadTemplate(): Promise<void> {
  downloading.value = true;
  try {
    downloadBlob(await downloadImportTemplate(), "import-vorlage.zip");
  } catch (error) {
    showError(error);
  } finally {
    downloading.value = false;
  }
}

async function startImport(): Promise<void> {
  const chosen = file.value;
  if (!chosen) {
    showError(new ApiError("ArchiveRequired"));
    return;
  }
  try {
    const summary = await run(t("importRunning"), () => importArchive(chosen));
    file.value = null;
    problems.value = [];
    if (fileInput.value) {
      fileInput.value.value = "";
    }
    await queryClient.invalidateQueries();
    showMessage(
      t("importDone"),
      t("importSummary", [
        summary.recipients,
        summary.items,
        summary.graves,
        summary.itemLinks,
        summary.recipientLinks,
      ]),
      "info",
    );
  } catch (error) {
    if (error instanceof ImportRejectedError) {
      problems.value = error.problems;
      showMessage(t("importArchive"), t("importRejected"));
      return;
    }
    showError(error);
  }
}
</script>

<template>
  <PageHeader :title="t('importExportTitle')" />

  <div class="flex flex-col gap-6">
    <Card>
      <template #title>{{ t("importTemplateHeader") }}</template>
      <template #content>
        <p class="mt-0 text-muted-color">{{ t("importTemplateHint") }}</p>
        <Button
          :label="t('importTemplateDownload')"
          icon="pi pi-download"
          outlined
          :loading="downloading"
          @click="downloadTemplate"
        />
      </template>
    </Card>

    <Card>
      <template #title>{{ t("importUploadHeader") }}</template>
      <template #content>
        <p class="mt-0 text-muted-color">{{ t("importUploadHint") }}</p>
        <div class="flex flex-wrap items-center gap-3">
          <input
            ref="fileInput"
            type="file"
            accept=".zip,application/zip"
            class="sr-only"
            tabindex="-1"
            aria-hidden="true"
            @change="onFileChosen"
          />
          <Button
            :label="t('importSelectFile')"
            icon="pi pi-folder-open"
            severity="secondary"
            outlined
            @click="chooseFile"
          />
          <span class="text-sm" :class="file ? '' : 'text-muted-color'">{{
            file?.name ?? t("noFileChosen")
          }}</span>
          <Button :label="t('importStart')" icon="pi pi-upload" @click="startImport" />
        </div>
      </template>
    </Card>

    <Card v-if="problems.length">
      <template #title>{{ t("importProblemsHeader") }}</template>
      <template #content>
        <DataTable :value="problems" size="small">
          <Column field="file" :header="t('importProblemFile')" />
          <Column field="row" :header="t('importProblemRow')" class="w-24" />
          <Column :header="t('importProblemMessage')">
            <template #body="{ data }">{{ problemText(data) }}</template>
          </Column>
        </DataTable>
      </template>
    </Card>
  </div>
</template>
