import BusyDialog from "sap/m/BusyDialog";
import type { Button$PressEvent } from "sap/m/Button";
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import type { SearchField$SearchEvent } from "sap/m/SearchField";
import TypedJSONModel from "sap/ui/model/json/TypedJSONModel";
import * as api from "../model/api";
import type { GenerateDraft, GravesStatus, GravesView } from "../model/state";
import GraveEdit from "./GraveEdit.controller";

export default class Graves extends GraveEdit {
  private static readonly JOB_TIMEOUT_MS = 10 * 60 * 1000;

  public onInit(): void {
    this.bindODataRoot();
    // The overview lists graves that are still open for the selected half-year
    // first (FR-028). That order depends on the half-year, so the rows come from
    // the status endpoint rather than from a plain Graves list binding.
    this.setState<GravesStatus>("status", { graves: [] });

    // half-year options are generated at runtime; default to the half-year today
    // falls in, the one the generate dialog and the grave detail page also open
    // on, so all three agree on which graves are still to be invoiced
    const [year, half] = api.currentHalfYear();
    this.setState<GravesView>("view", {
      halfYearOptions: api.halfYearOptions(2, 1),
      halfYear: `${year}-${half}`,
    });

    void this.refreshStatus();
  }

  /**
   * What a screen reader says about the status. Without it the red state is
   * announced as a validation error, which an open half-year is not.
   */
  public statusAnnouncement(invoiced: boolean | null | undefined): string {
    return this.i18nText(invoiced ? "statusInvoicedAnnouncement" : "statusOpenAnnouncement");
  }

  public statusFormatter(invoiced: boolean | null | undefined): string {
    return this.i18nText(invoiced ? "statusInvoiced" : "statusOpen");
  }

  public onHalfYearChange(): void {
    void this.refreshStatus();
  }

  public async onSearchGraves(event: SearchField$SearchEvent): Promise<void> {
    // the backend search is Unicode-aware (umlauts), so it decides which graves
    // match; the ids it returns narrow the rows the overview already holds
    this._searchIds = null;
    const term = event.getParameter("query") ?? "";
    if (term) {
      try {
        const matches = await api.searchGraves(term);
        this._searchIds = new Set(matches.map((match) => Number(match.Id)));
      } catch (error) {
        api.showError(this, error);
        return;
      }
    }

    this.applyRows();
  }

  /** "2026-1" -> [2026, 1]: the half-year the toolbar shows. */
  private selectedHalfYear(): [number, number] {
    const [year, half] = this.state<GravesView>("view")
      .getProperty("/halfYear")
      .split("-")
      .map(Number);
    return [year, half];
  }

  protected async refreshStatus(): Promise<void> {
    const [year, half] = this.selectedHalfYear();
    try {
      this._rows = await api.fetchGraveStatus(year, half);
      this.applyRows();
    } catch (error) {
      api.showError(this, error);
    }
  }

  /// The rows the table shows: the status order, narrowed to the last search.
  private applyRows(): void {
    const rows = this._searchIds
      ? this._rows.filter((row) => this._searchIds!.has(Number(row.Id)))
      : this._rows;
    this.state<GravesStatus>("status").setProperty("/graves", rows);
  }

  public async onCreateGrave(): Promise<void> {
    await this.openGraveDialog(null);
  }

  public async onEditGrave(event: Button$PressEvent): Promise<void> {
    const context = event.getSource().getBindingContext("status")!;
    await this.openGraveDialog(Number(context.getProperty("Id")));
  }

  public onShowGraveDetails(event: Button$PressEvent): void {
    const context = event.getSource().getBindingContext("status")!;
    this.router().navTo("graveDetail", {
      graveId: String(context.getProperty("Id")),
    });
  }

  /**
   * The normal documents of the half-year the toolbar shows, across all graves.
   * Copies are downloaded one at a time from the detail pages; a bulk of copies
   * is the one thing that could be mistaken for a bulk of originals.
   */
  public async onBulkGraveInvoices(): Promise<void> {
    const [year, half] = this.selectedHalfYear();
    try {
      await api.bulkDownload("Normal", "rechnungen.zip", year, half);
    } catch (error) {
      api.showError(this, error);
    }
  }

  protected async refreshAfterSave(): Promise<void> {
    await this.refreshStatus();
  }

  public async onGenerateInvoices(): Promise<void> {
    if (this._running) {
      return;
    }

    if (!this._generateDialog) {
      this._generateDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.GenerateDialog",
      })) as Dialog;
    }

    // year options are generated at runtime; the dialog opens on the half-year
    // the toolbar shows, so a run without further input fills in the period the
    // status column is reporting on
    const [year, half] = api.currentHalfYear();
    this._generateDialog.setModel(
      new TypedJSONModel<GenerateDraft>({
        yearOptions: api.yearOptions(1, 1),
        year: String(year),
        half: String(half),
      }),
      "generateDraft",
    );
    this._generateDialog.open();
  }

  public async onGenerateDialogSave(): Promise<void> {
    const draft = (
      this._generateDialog.getModel("generateDraft") as TypedJSONModel<GenerateDraft>
    ).getData();
    const year = Number(draft.year);
    const half = Number(draft.half);

    this._running = true;
    this._generateDialog.close();
    const busy = new BusyDialog({ title: this.i18nText("generationRunning") });
    busy.open();

    let finished: api.JobDto | null = null;
    let failure: unknown = null;
    try {
      const job = await api.generateGraveInvoices(year, half);
      finished = await this.pollJob(job.Id, (percent) =>
        busy.setTitle(this.i18nText("generationProgress").replace("{0}", String(percent))),
      );
    } catch (error) {
      failure = error;
    } finally {
      // close the busy dialog before any result or error message opens
      busy.close();
      this._running = false;
    }

    if (failure) {
      api.showError(this, failure);
      return;
    }

    if (!finished) {
      return;
    }

    if (finished.Status === "Failed") {
      MessageBox.error(this.i18nText("generationFailed"));
      return;
    }

    const result = JSON.parse(finished.ResultJson ?? "{}") as api.GenerationResult;
    const skipped = (result.SkippedGraves ?? [])
      .map((entry) => {
        const number = typeof entry === "string" ? entry : entry.Number;
        const reason =
          typeof entry === "object" && entry.Reason
            ? this.i18nText(`skipReason${entry.Reason}`)
            : "";
        return reason ? `${number} (${reason})` : String(number);
      })
      .join(", ");
    const message =
      `${this.i18nText("generationDone")}: ${result.InvoicedGraves ?? 0}` +
      (skipped ? ` (${this.i18nText("skippedGraves")}: ${skipped})` : "");
    // non-blocking: the busy dialog must close while the result message
    // stays open for the user to dismiss
    MessageBox.information(message);
    await this.refreshStatus();
  }

  public onGenerateDialogCancel(): void {
    this._generateDialog.close();
  }

  private async pollJob(jobId: number, onProgress: (percent: number) => void): Promise<api.JobDto> {
    const deadline = Date.now() + Graves.JOB_TIMEOUT_MS;
    for (;;) {
      if (Date.now() > deadline) {
        throw new Error("JobTimeout");
      }

      const response = await fetch(`/odata/Jobs(${jobId})`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const job = (await response.json()) as api.JobDto;
      if (job.Status === "Succeeded" || job.Status === "Failed") {
        return job;
      }

      onProgress(job.ProgressPercent ?? 0);

      await new Promise((resolve) => setTimeout(resolve, 1000));
    }
  }

  private _rows: api.GraveStatusRow[] = [];
  private _searchIds: Set<number> | null = null;
  private _running = false;
  private _generateDialog!: Dialog;
}
