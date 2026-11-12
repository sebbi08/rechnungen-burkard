<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import ConfirmDialog from "primevue/confirmdialog";
import DataTable from "primevue/datatable";
import Message from "primevue/message";
import { useConfirm } from "primevue/useconfirm";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import {
  type BackupSummary,
  discardBackup,
  downloadBackup,
  downloadImportTemplate,
  importArchive,
  type ImportProblem,
  ImportRejectedError,
  restoreBackup,
  uploadBackup,
} from "@/api/endpoints";
import { ApiError } from "@/api/http";
import PageHeader from "@/components/PageHeader.vue";
import { useBlocking } from "@/composables/useBlocking";
import { useErrorToast } from "@/composables/useErrorToast";
import { useShortTablePaging } from "@/composables/usePageReport";
import { showMessage, showMessageAndWait } from "@/composables/useMessage";
import { downloadBlob } from "@/lib/download";
import { formatAmount, formatCount, formatDateTime, formatVatRate } from "@/lib/format";

const i18n = useI18n();
const { t } = i18n;
const paging = useShortTablePaging();
const { showError } = useErrorToast();
const { run } = useBlocking();
const queryClient = useQueryClient();
const confirm = useConfirm();

const fileInput = ref<HTMLInputElement>();
const file = ref<File | null>(null);
const problems = ref<ImportProblem[]>([]);
const downloading = ref(false);
const backupInput = ref<HTMLInputElement>();
const downloadingBackup = ref(false);
/** What the restore confirmation shows, while it is open. */
const restoreQuestion = ref<{ summary: string; newerInvoicesHere: boolean } | null>(null);

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
  if (problem.messageKey === "PricesDoNotDivideIn") {
    args = args.map((arg, index) =>
      index === 1 ? formatVatRate(Number(arg)) : index === 2 ? formatAmount(Number(arg)) : arg,
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

async function downloadBackupFile(): Promise<void> {
  downloadingBackup.value = true;
  try {
    const { blob, fileName } = await downloadBackup();
    downloadBlob(blob, fileName);
  } catch (error) {
    showError(error);
  } finally {
    downloadingBackup.value = false;
  }
}

/** Clears the input first, as chooseFile does: the same file twice fires no change. */
function chooseBackup(): void {
  if (backupInput.value) {
    backupInput.value.value = "";
    backupInput.value.click();
  }
}

/** Asks before all data is replaced. A dialog of its own rather than the
 *  yes/no one: Enter must not replace everything, so "Abbrechen" has the
 *  focus; the button says what it does; and the newer-invoices warning
 *  stands apart from the summary. */
function confirmRestore(summary: BackupSummary): Promise<boolean> {
  restoreQuestion.value = {
    summary: t("backupConfirm", [
      formatDateTime(summary.createdAt),
      summary.appVersion,
      formatCount(summary.graves),
      formatCount(summary.recipients),
      formatCount(summary.invoices),
      formatCount(summary.quotes),
    ]),
    newerInvoicesHere: summary.newerInvoicesHere,
  };
  return new Promise((resolve) => {
    confirm.require({
      group: "backupRestore",
      header: t("confirmTitle"),
      defaultFocus: "reject",
      rejectProps: { label: t("cancel"), severity: "secondary", outlined: true },
      acceptProps: { label: t("backupReplaceAll"), severity: "danger" },
      accept: () => resolve(true),
      reject: () => resolve(false),
      onHide: () => resolve(false),
    });
  });
}

/** Uploads the chosen backup for checking, asks, then replaces all data with it. */
async function onBackupChosen(event: Event): Promise<void> {
  const chosen = (event.target as HTMLInputElement).files?.[0];
  if (!chosen) {
    return;
  }
  let token: string | null = null;
  try {
    const staged = await run(t("backupChecking"), () => uploadBackup(chosen));
    token = staged.token;
    if (!(await confirmRestore(staged.summary))) {
      await discardBackup(token).catch(() => undefined);
      return;
    }
    await run(t("backupRestoring"), () => restoreBackup(staged.token));
    // the reload waits for the owner to dismiss the message, or it is never seen
    await showMessageAndWait(t("backupHeader"), t("backupRestored"), "info");
    window.location.reload();
  } catch (error) {
    showError(error);
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
    const counts = t("importSummary", [
      summary.recipients,
      summary.items,
      summary.graves,
      summary.itemLinks,
      summary.recipientLinks,
    ]);
    showMessage(
      t("importDone"),
      summary.correctedGraves > 0
        ? `${counts}, ${t("importCorrectedGraves", [summary.correctedGraves])}`
        : counts,
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

  <ConfirmDialog group="backupRestore" :style="{ maxWidth: 'min(32rem, calc(100vw - 2rem))' }">
    <template #message>
      <div v-if="restoreQuestion" class="flex flex-col gap-3">
        <p class="m-0">{{ restoreQuestion.summary }}</p>
        <p class="m-0 font-semibold">{{ t("backupConfirmReplace") }}</p>
        <Message v-if="restoreQuestion.newerInvoicesHere" severity="warn" :closable="false">
          {{ t("backupConfirmNewer") }}
        </Message>
      </div>
    </template>
  </ConfirmDialog>

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

    <Card>
      <template #title>{{ t("backupHeader") }}</template>
      <template #content>
        <p class="mt-0 text-muted-color">{{ t("backupHint") }}</p>
        <div class="flex flex-wrap items-center gap-3">
          <input
            ref="backupInput"
            type="file"
            accept=".zip,application/zip"
            class="sr-only"
            tabindex="-1"
            aria-hidden="true"
            @change="onBackupChosen"
          />
          <Button
            :label="t('backupDownload')"
            icon="pi pi-download"
            outlined
            :loading="downloadingBackup"
            @click="downloadBackupFile"
          />
          <Button
            :label="t('backupRestore')"
            icon="pi pi-upload"
            severity="secondary"
            outlined
            @click="chooseBackup"
          />
        </div>
      </template>
    </Card>

    <Card v-if="problems.length">
      <template #title>{{ t("importProblemsHeader") }}</template>
      <template #content>
        <DataTable :value="problems" v-bind="paging" size="small">
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
