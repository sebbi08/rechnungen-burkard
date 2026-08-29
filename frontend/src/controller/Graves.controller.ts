import BusyDialog from "sap/m/BusyDialog";
import MessageBox from "sap/m/MessageBox";
import Select from "sap/m/Select";
import Item from "sap/ui/core/Item";
import JSONModel from "sap/ui/model/json/JSONModel";
import * as api from "../model/api";
import GraveEdit from "./GraveEdit.controller";

export default class Graves extends GraveEdit {
  private static readonly JOB_TIMEOUT_MS = 10 * 60 * 1000;

  public onInit(): void {
    const view = this.getView()!;
    this.bindODataRoot();
    // The overview lists graves that are still open for the selected half-year
    // first (FR-028). That order depends on the half-year, so the rows come from
    // the status endpoint rather than from a plain Graves list binding.
    view.setModel(new JSONModel({ graves: [] }), "status");

    // half-year options are generated at runtime; default to the current
    // year's first half, matching the generate dialog's default half
    const select = this.byId("halfYearSelect") as Select;
    select.removeAllItems();
    for (const option of api.halfYearOptions(2, 1)) {
      select.addItem(new Item({ key: option.key, text: option.text }));
    }
    select.setSelectedKey(`${new Date().getFullYear()}-1`);

    void this.refreshStatus();
  }

  public statusFormatter(invoiced: boolean | null | undefined): string {
    return this.i18nText(invoiced ? "statusInvoiced" : "statusOpen");
  }

  public onHalfYearChange(): void {
    void this.refreshStatus();
  }

  public async onSearchGraves(event: any): Promise<void> {
    // the backend search is Unicode-aware (umlauts), so it decides which graves
    // match; the ids it returns narrow the rows the overview already holds
    this._searchIds = null;
    const term = event.getParameter("query") as string;
    if (term) {
      try {
        const matches = await api.searchGraves(term);
        this._searchIds = new Set(matches.map((match) => Number(match.Id)));
      } catch (error) {
        await api.showError(this, error);
        return;
      }
    }

    this.applyRows();
  }

  protected async refreshStatus(): Promise<void> {
    const [year, half] = (this.byId("halfYearSelect") as Select)
      .getSelectedKey()
      .split("-")
      .map(Number);
    try {
      this._rows = await api.fetchGraveStatus(year, half);
      this.applyRows();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  /// The rows the table shows: the status order, narrowed to the last search.
  private applyRows(): void {
    const rows = this._searchIds
      ? this._rows.filter((row) => this._searchIds!.has(Number(row.Id)))
      : this._rows;
    (this.getView()!.getModel("status") as JSONModel).setProperty(
      "/graves",
      rows,
    );
  }

  public async onCreateGrave(): Promise<void> {
    await this.openGraveDialog(null);
  }

  public async onEditGrave(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("status");
    await this.openGraveDialog(Number(context.getProperty("Id")));
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
      })) as any;
    }

    // year options are generated at runtime; default to the current year
    const yearSelect = this.byId("genYear") as Select;
    yearSelect.removeAllItems();
    for (const option of api.yearOptions(1, 1)) {
      yearSelect.addItem(new Item({ key: option.key, text: option.text }));
    }
    yearSelect.setSelectedKey(String(new Date().getFullYear()));

    this._generateDialog.open();
  }

  public async onGenerateDialogSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    const year = Number((this.byId("genYear") as Select).getSelectedKey());
    const half = Number((this.byId("genHalf") as Select).getSelectedKey());

    this._running = true;
    dialog.close();
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
      await api.showError(this, failure);
      return;
    }

    if (!finished) {
      return;
    }

    if (finished.Status === "Failed") {
      await MessageBox.error(this.i18nText("generationFailed"));
      return;
    }

    const result = JSON.parse(finished.ResultJson ?? "{}") as any;
    const skipped = (result.SkippedGraves ?? [])
      .map((entry: any) => {
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
      (skipped
        ? ` (${this.i18nText("skippedGraves")}: ${skipped})`
        : "");
    // non-blocking: the busy dialog must close while the result message
    // stays open for the user to dismiss
    void MessageBox.information(message);
    await this.refreshStatus();
  }

  public onGenerateDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  private async pollJob(
    jobId: number,
    onProgress: (percent: number) => void,
  ): Promise<api.JobDto> {
    const deadline = Date.now() + Graves.JOB_TIMEOUT_MS;
    while (true) {
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
  private _generateDialog: any;
}
