import BusyDialog from "sap/m/BusyDialog";
import Input from "sap/m/Input";
import MessageBox from "sap/m/MessageBox";
import Select from "sap/m/Select";
import TextArea from "sap/m/TextArea";
import Item from "sap/ui/core/Item";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import JSONModel from "sap/ui/model/json/JSONModel";
import Context from "sap/ui/model/odata/v4/Context";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import * as api from "../model/api";
import BaseController from "./Base.controller";

export default class Graves extends BaseController {
  private static readonly JOB_TIMEOUT_MS = 10 * 60 * 1000;

  public onInit(): void {
    const view = this.getView()!;
    // routed views run onInit before they are added to the App, so the
    // odata model is not visible via view.getModel() yet
    const model = this.getOwnerComponent()!.getModel("odata") as ODataModel;
    view.setBindingContext(model.createBindingContext("/") as Context, "odata");
    view.setModel(new JSONModel({ statuses: {} }), "status");

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

  public statusFormatter(
    graveId: number,
    statuses: Record<string, any>,
  ): string {
    const bundle = (
      this.getView()!.getModel("i18n") as any
    ).getResourceBundle();
    return statuses?.[String(graveId)]?.Invoiced
      ? bundle.getText("statusInvoiced")
      : bundle.getText("statusOpen");
  }

  public formatDate(value: Date | string | null | undefined): string {
    if (value == null) {
      return "";
    }
    const date = value instanceof Date ? value : new Date(value);
    return date.toLocaleDateString("de-DE");
  }

  public onHalfYearChange(): void {
    void this.refreshStatus();
  }

  public async onSearchGraves(event: any): Promise<void> {
    const term = event.getParameter("query") as string;
    const binding = (this.byId("gravesTable") as any).getBinding(
      "items",
    ) as ODataListBinding;
    if (!term) {
      binding.filter([]);
      return;
    }

    try {
      // the backend search is Unicode-aware (umlauts); filter the table
      // by the returned numeric ids, which are immune to case folding
      const matches = await api.searchGraves(term);
      const ids = matches.map((match) => Number(match.id));
      if (!ids.length) {
        // no match: filter to an impossible id so the table renders empty
        binding.filter(new Filter("Id", FilterOperator.EQ, -1));
        return;
      }

      binding.filter(
        new Filter({
          filters: ids.map((id) => new Filter("Id", FilterOperator.EQ, id)),
          and: false,
        }),
      );
    } catch (error) {
      await api.showError(this, error);
    }
  }

  private async refreshStatus(): Promise<void> {
    const [year, half] = (this.byId("halfYearSelect") as Select)
      .getSelectedKey()
      .split("-")
      .map(Number);
    try {
      const rows = await api.fetchGraveStatus(year, half);
      const statuses: Record<string, any> = {};
      for (const row of rows) {
        statuses[String(row.Id)] = row;
      }

      (this.getView()!.getModel("status") as JSONModel).setProperty(
        "/statuses",
        statuses,
      );
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onCreateGrave(): Promise<void> {
    await this.openGraveDialog(null);
  }

  public async onEditGrave(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openGraveDialog(context);
  }

  private async openGraveDialog(graveContext: Context | null): Promise<void> {
    if (!this._graveDialog) {
      // loadFragment passes this controller and the view ID prefix so that
      // this.byId(...) finds the dialog controls and IDs stay unique
      this._graveDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.GraveDialog",
      })) as any;
    }

    if (graveContext) {
      const details = await api.fetchGraveDetails(
        graveContext.getProperty("Id") as number,
      );
      this._graveDialog.setModel(
        new JSONModel({
          Id: details.Id,
          GraveNumber: details.GraveNumber,
          StartDate: details.StartDate,
          EndDate: details.EndDate,
          Items: details.Items.map((link: any) => ({
            Id: link.Id,
            Number: link.Item.Number,
            Count: link.Count,
          })),
          Recipients: details.Recipients.map((link: any) => ({
            Id: link.Id,
            Name: link.Recipient.Name,
          })),
        }),
        "graveDraft",
      );
    } else {
      this._graveDialog.setModel(
        new JSONModel({ Items: [], Recipients: [] }),
        "graveDraft",
      );
    }

    this._graveDialog.open();
  }

  public async onGraveDialogSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    const draft = (dialog.getModel("graveDraft") as JSONModel).getData() as any;
    try {
      if (draft.Id) {
        await api.patchGrave(Number(draft.Id), {
          GraveNumber: (this.byId("graveNumber") as Input).getValue(),
          StartDate: (this.byId("graveStart") as any).getValue(),
          EndDate: (this.byId("graveEnd") as any).getValue(),
        });
      } else {
        await api.createGrave({
          GraveNumber: (this.byId("graveNumber") as Input).getValue(),
          StartDate: (this.byId("graveStart") as any).getValue(),
          EndDate: (this.byId("graveEnd") as any).getValue(),
        });
      }

      dialog.close();
      (
        (this.byId("gravesTable") as any).getBinding(
          "items",
        ) as ODataListBinding
      ).refresh();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onGraveDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  public async onAddItemLink(): Promise<void> {
    const draft = this._graveDialog.getModel("graveDraft") as JSONModel;
    const graveId = draft.getProperty("/Id");
    if (!graveId) {
      await api.showError(this, new Error("GraveNotSaved"));
      return;
    }

    // an item without a default price would make the whole grave get skipped
    // during invoice generation, so reject the link right away
    const linkItem = this.byId("linkItem") as Select;
    const itemKey = linkItem.getSelectedKey();
    const contexts = (linkItem.getBinding("items") as ODataListBinding).getContexts() as Context[];
    const itemContext = contexts.find(
      (context) => String(context.getProperty("Id")) === itemKey,
    );
    if (itemContext && itemContext.getProperty("DefaultPrice") == null) {
      await api.showError(this, new Error("PriceRequired"));
      return;
    }

    try {
      await api.createGraveItemLink(
        Number(graveId),
        Number(itemKey),
        Number((this.byId("linkCount") as Input).getValue()),
      );
      await this.reloadGraveLinks();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onAddRecipientLink(): Promise<void> {
    const draft = this._graveDialog.getModel("graveDraft") as JSONModel;
    const graveId = draft.getProperty("/Id");
    if (!graveId) {
      await api.showError(this, new Error("GraveNotSaved"));
      return;
    }

    try {
      await api.createGraveRecipientLink(
        Number(graveId),
        Number((this.byId("linkRecipient") as Select).getSelectedKey()),
      );
      await this.reloadGraveLinks();
    } catch (error) {
      await api.showError(this, error);
    }
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

  public async onDeleteItemLink(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("graveDraft") as any;
    try {
      await api.deleteGraveItemLink(context.getProperty("Id") as number);
      await this.reloadGraveLinks();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onDeleteRecipientLink(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("graveDraft") as any;
    try {
      await api.deleteGraveRecipientLink(context.getProperty("Id") as number);
      await this.reloadGraveLinks();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  private async reloadGraveLinks(): Promise<void> {
    const draft = this._graveDialog.getModel("graveDraft") as JSONModel;
    const details = await api.fetchGraveDetails(
      Number(draft.getProperty("/Id")),
    );
    draft.setProperty(
      "/Items",
      details.Items.map((link: any) => ({
        Id: link.Id,
        Number: link.Item.Number,
        Count: link.Count,
      })),
    );
    draft.setProperty(
      "/Recipients",
      details.Recipients.map((link: any) => ({
        Id: link.Id,
        Name: link.Recipient.Name,
      })),
    );
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
      finished = await this.pollJob(job.Id);
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

  private async pollJob(jobId: number): Promise<api.JobDto> {
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

      await new Promise((resolve) => setTimeout(resolve, 1000));
    }
  }

  private _running = false;
  private _graveDialog: any;
  private _quickDialog: any;
  private _generateDialog: any;
}
