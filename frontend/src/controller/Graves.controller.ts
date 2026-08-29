import BusyDialog from "sap/m/BusyDialog";
import { ComboBox$SelectionChangeEvent } from "sap/m/ComboBox";
import Input from "sap/m/Input";
import MessageBox from "sap/m/MessageBox";
import Select from "sap/m/Select";
import TextArea from "sap/m/TextArea";
import Item from "sap/ui/core/Item";
import JSONModel from "sap/ui/model/json/JSONModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import * as api from "../model/api";
import BaseController from "./Base.controller";

export default class Graves extends BaseController {
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

  private async refreshStatus(): Promise<void> {
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

  private async openGraveDialog(graveId: number | null): Promise<void> {
    if (!this._graveDialog) {
      // loadFragment passes this controller and the view ID prefix so that
      // this.byId(...) finds the dialog controls and IDs stay unique
      this._graveDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.GraveDialog",
      })) as any;
    }

    // everything the dialog collects lives in this draft until it is saved, so
    // items and recipients can be added before the grave itself exists
    this._graveDialog.setModel(
      new JSONModel(
        graveId != null
          ? await this.loadGraveDraft(graveId)
          : {
              Id: null,
              GraveNumber: "",
              StartDate: "",
              EndDate: "",
              Items: [],
              Recipients: [],
            },
      ),
      "graveDraft",
    );

    this._graveDialog.open();
  }

  private async loadGraveDraft(graveId: number): Promise<any> {
    const details = await api.fetchGraveDetails(graveId);
    return {
      Id: details.Id,
      GraveNumber: details.GraveNumber,
      StartDate: details.StartDate,
      EndDate: details.EndDate,
      Items: details.Items.map((link: any) => ({
        // the ComboBox matches its keys as strings
        itemId: String(link.Item.Id),
        itemNumber: link.Item.Number,
        description: link.Item.Description,
        count: link.Count,
        unitPrice: link.UnitPrice,
        defaultPrice: link.Item.DefaultPrice,
      })),
      Recipients: details.Recipients.map((link: any) => ({
        recipientId: String(link.Recipient.Id),
        name: link.Recipient.Name,
      })),
    };
  }

  public onGraveItemSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes the key itself; fill in what the row shows alongside it
    const itemContext = event
      .getParameter("selectedItem")
      ?.getBindingContext("odata");
    const rowContext = event.getSource().getBindingContext("graveDraft");
    if (!itemContext || !rowContext) {
      return;
    }

    const model = rowContext.getModel() as JSONModel;
    const path = rowContext.getPath();
    model.setProperty(
      path + "/itemNumber",
      itemContext.getProperty("Number") as string,
    );
    model.setProperty(
      path + "/description",
      itemContext.getProperty("Description") as string,
    );
    // an empty price falls back to this, which the input shows as its placeholder
    model.setProperty(
      path + "/defaultPrice",
      itemContext.getProperty("DefaultPrice"),
    );
  }

  public onAddItemLink(): void {
    const model = this._graveDialog.getModel("graveDraft") as JSONModel;
    const rows = (model.getProperty("/Items") as any[]) ?? [];
    rows.push({
      itemId: "",
      itemNumber: "",
      description: "",
      count: 1,
      unitPrice: null,
      defaultPrice: null,
    });
    model.setProperty("/Items", rows);
  }

  public async onAddRecipientLink(): Promise<void> {
    const select = this.byId("linkRecipient") as Select;
    const selected = select.getSelectedItem();
    if (!selected) {
      return;
    }

    const model = this._graveDialog.getModel("graveDraft") as JSONModel;
    const rows = (model.getProperty("/Recipients") as any[]) ?? [];
    const recipientId = select.getSelectedKey();
    if (rows.some((row) => row.recipientId === recipientId)) {
      await api.showError(this, new Error("GraveRecipientLinkExists"));
      return;
    }

    rows.push({ recipientId, name: selected.getText() });
    model.setProperty("/Recipients", rows);
  }

  public onDeleteItemLink(event: any): void {
    this.removeDraftRow(event, "Items");
  }

  public onDeleteRecipientLink(event: any): void {
    this.removeDraftRow(event, "Recipients");
  }

  private removeDraftRow(event: any, collection: string): void {
    const context = event.getSource().getBindingContext("graveDraft") as any;
    const model = context.getModel() as JSONModel;
    const rows = model.getProperty(`/${collection}`) as any[];
    // the row's binding path ends in its index, e.g. "/Items/2"
    rows.splice(Number(context.getPath().split("/").pop()), 1);
    model.setProperty(`/${collection}`, rows);
  }

  public async onGraveDialogSave(): Promise<void> {
    const model = this._graveDialog.getModel("graveDraft") as JSONModel;
    const draft = model.getData() as any;
    const items = draft.Items as any[];
    if (items.some((row) => !row.itemId)) {
      await api.showError(this, new Error("ItemRequired"));
      return;
    }

    try {
      await api.saveGrave({
        graveId: draft.Id != null ? Number(draft.Id) : null,
        graveNumber: draft.GraveNumber ?? "",
        startDate: draft.StartDate ?? "",
        endDate: draft.EndDate ?? "",
        items: items.map((row) => ({
          itemId: Number(row.itemId),
          count: Number(row.count),
          // a price of 0 is a real value, not a missing one
          unitPrice:
            row.unitPrice == null || row.unitPrice === ""
              ? null
              : Number(row.unitPrice),
        })),
        recipientIds: (draft.Recipients as any[]).map((row) =>
          Number(row.recipientId),
        ),
      });

      this._graveDialog.close();
      await this.refreshStatus();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onGraveDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  public async onNewRecipient(): Promise<void> {
    if (!this._quickDialog) {
      // loadFragment passes this controller and the view ID prefix so that
      // this.byId(...) finds the quick-create dialog controls
      this._quickDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.RecipientQuickCreate",
      })) as any;
    }
    (this.byId("quickName") as Input).setValue("");
    (this.byId("quickAddress") as TextArea).setValue("");
    this._quickDialog.open();
  }

  public async onQuickRecipientSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    try {
      const id = await api.createRecipient({
        Name: (this.byId("quickName") as Input).getValue(),
        Address: (this.byId("quickAddress") as TextArea).getValue(),
      });
      // refresh the link select's list so the new row is included, then
      // select it; the parent grave dialog stays open
      const select = this.byId("linkRecipient") as Select;
      await (select.getBinding("items") as ODataListBinding).refresh();
      select.setSelectedKey(String(id));
      dialog.close();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onQuickRecipientCancel(event: any): void {
    (event.getSource().getParent() as any).close();
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
  private _graveDialog: any;
  private _quickDialog: any;
  private _generateDialog: any;
}
