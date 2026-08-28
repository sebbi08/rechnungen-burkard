import BusyDialog from "sap/m/BusyDialog";
import MessageBox from "sap/m/MessageBox";
import Panel from "sap/m/Panel";
import JSONModel from "sap/ui/model/json/JSONModel";
import type FileUploader from "sap/ui/unified/FileUploader";
import type { FileUploader$ChangeEvent } from "sap/ui/unified/FileUploader";
import * as api from "../model/api";
import BaseController from "./Base.controller";

export default class ImportExport extends BaseController {
  public onInit(): void {
    this.getView()!.setModel(new JSONModel({ problems: [] }), "import");
  }

  public formatRow(row: number | null | undefined): string {
    return row == null ? "" : String(row);
  }

  public async onDownloadTemplate(): Promise<void> {
    try {
      await api.downloadImportTemplate("import-vorlage.zip");
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onFileSelected(event: FileUploader$ChangeEvent): void {
    // a new file supersedes whatever the last attempt reported
    this._file = (event.getParameter("files")?.[0] as File | undefined) ?? null;
    this.showProblems([]);
  }

  public async onImport(): Promise<void> {
    if (this._running) {
      return;
    }

    if (!this._file) {
      await api.showError(this, new Error("ArchiveRequired"));
      return;
    }

    // long-running task: block the interface until it finishes, so the same
    // archive cannot be imported twice (FR-063)
    this._running = true;
    const busy = new BusyDialog({ title: this.i18nText("importRunning") });
    busy.open();

    let summary: api.ImportSummary | null = null;
    let failure: unknown = null;
    try {
      summary = await api.importArchive(this._file);
    } catch (error) {
      failure = error;
    } finally {
      // close the busy dialog before any result or error message opens
      busy.close();
      this._running = false;
    }

    if (summary) {
      this.showProblems([]);
      this.reset();
      await MessageBox.information(
        `${this.i18nText("importDone")}\n${this.formatSummary(summary)}`,
      );
      return;
    }

    if (failure instanceof api.ImportRejectedError) {
      this.showProblems(failure.problems);
      await MessageBox.error(this.i18nText("importRejected"));
      return;
    }

    await api.showError(this, failure);
  }

  private formatSummary(summary: api.ImportSummary): string {
    return this.i18nText("importSummary")
      .replace("{0}", String(summary.recipients))
      .replace("{1}", String(summary.items))
      .replace("{2}", String(summary.graves))
      .replace("{3}", String(summary.itemLinks))
      .replace("{4}", String(summary.recipientLinks));
  }

  private showProblems(problems: api.ImportProblem[]): void {
    (this.getView()!.getModel("import") as JSONModel).setProperty(
      "/problems",
      problems,
    );
    (this.byId("problemsPanel") as Panel).setVisible(problems.length > 0);
  }

  private reset(): void {
    this._file = null;
    (this.byId("archiveUpload") as FileUploader).clear();
  }

  private _file: File | null = null;
  private _running = false;
}
