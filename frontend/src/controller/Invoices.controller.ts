import type ResourceBundle from "sap/base/i18n/ResourceBundle";
import BusyDialog from "sap/m/BusyDialog";
import { ComboBox$SelectionChangeEvent } from "sap/m/ComboBox";
import Input from "sap/m/Input";
import Select from "sap/m/Select";
import TextArea from "sap/m/TextArea";
import Item from "sap/ui/core/Item";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import JSONModel from "sap/ui/model/json/JSONModel";
import Context from "sap/ui/model/odata/v4/Context";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import ResourceModel from "sap/ui/model/resource/ResourceModel";
import * as api from "../model/api";
import BaseController from "./Base.controller";

export default class Invoices extends BaseController {
  public onInit(): void {
    this.bindODataRoot();

    // half-year filter options are generated at runtime
    const select = this.byId("halfYearFilter") as Select;
    const bundle = (
      this.getOwnerComponent()!.getModel("i18n") as ResourceModel
    ).getResourceBundle() as ResourceBundle;
    select.removeAllItems();
    select.addItem(new Item({ key: "", text: bundle.getText("allInvoices") }));
    for (const option of api.halfYearOptions(2, 1)) {
      select.addItem(new Item({ key: option.key, text: option.text }));
    }
  }

  public onHalfYearChange(event: any): void {
    const key = (event.getSource() as Select).getSelectedKey();
    const binding = (this.byId("invoicesTable") as any).getBinding(
      "items",
    ) as ODataListBinding;
    if (!key) {
      binding.filter([]);
      return;
    }

    const [year, half] = key.split("-").map(Number);
    binding.filter(
      new Filter({
        filters: [
          new Filter("PeriodYear", FilterOperator.EQ, year),
          new Filter("PeriodHalf", FilterOperator.EQ, half),
        ],
        and: true,
      }),
    );
  }

  public async onCreateInvoice(): Promise<void> {
    await this.openInvoiceDialog(null);
  }

  public async onEditInvoice(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openInvoiceDialog(context);
  }

  private async openInvoiceDialog(
    invoiceContext: Context | null,
  ): Promise<void> {
    if (!this._dialog) {
      // loadFragment passes this controller and the view ID prefix so that
      // this.byId(...) finds the dialog controls and IDs stay unique
      this._dialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.InvoiceDialog",
      })) as any;
    }

    const lines = invoiceContext
      ? await this.loadLines(invoiceContext.getProperty("Id") as number)
      : [Invoices.emptyLine()];
    this._dialog.setModel(
      new JSONModel({
        recipientId: invoiceContext?.getProperty("RecipientId") ?? null,
        invoiceId: invoiceContext?.getProperty("Id") ?? null,
        lines,
      }),
      "invoiceDraft",
    );
    this._dialog.open();
  }

  private async loadLines(invoiceId: number): Promise<any[]> {
    try {
      const response = await fetch(`/odata/Invoices(${invoiceId})/Lines`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      const body = await response.json();
      return (body.value as any[]).map((line) => ({
        // the ComboBox matches its keys as strings
        itemId: line.ItemId != null ? String(line.ItemId) : "",
        itemNumber: line.ItemNumber,
        count: line.Count,
        unitPrice: line.UnitPrice,
        defaultPrice: null,
        description: line.Description,
      }));
    } catch (error) {
      await api.showError(this, error);
      return [Invoices.emptyLine()];
    }
  }

  public onLineItemSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes its key into the draft itself; fill in what the row
    // shows alongside it
    const selected = event.getParameter("selectedItem");
    const rowContext = event.getSource().getBindingContext("invoiceDraft");
    if (!selected || !rowContext) {
      return;
    }
    const itemContext = selected.getBindingContext("odata");
    if (!itemContext) {
      return;
    }

    const path = rowContext.getPath();
    const model = rowContext.getModel() as JSONModel;
    model.setProperty(
      path + "/itemNumber",
      itemContext.getProperty("Number") as string,
    );
    model.setProperty(
      path + "/description",
      itemContext.getProperty("Description") as string,
    );
    // the item's default price is pre-filled and stays overridable; an item
    // without one leaves the field empty so a price has to be typed
    const defaultPrice = itemContext.getProperty("DefaultPrice");
    model.setProperty(path + "/defaultPrice", defaultPrice);
    model.setProperty(
      path + "/unitPrice",
      defaultPrice != null ? Number(defaultPrice) : null,
    );
  }

  public onAddLine(): void {
    const model = this._dialog.getModel("invoiceDraft") as JSONModel;
    const lines = (model.getProperty("/lines") as any[]) ?? [];
    lines.push(Invoices.emptyLine());
    model.setProperty("/lines", lines);
  }

  private static emptyLine(): Record<string, unknown> {
    return {
      itemId: "",
      itemNumber: "",
      count: 1,
      unitPrice: null,
      defaultPrice: null,
      description: "",
    };
  }

  public async onInvoiceDialogSave(): Promise<void> {
    // a second press while the first invoice is still being created would
    // create a second one; the busy dialog below blocks the interface, this
    // guard covers the moment before it is on screen
    if (this._saving) {
      return;
    }

    const model = this._dialog.getModel("invoiceDraft") as JSONModel;
    const draft = model.getData() as any;
    const rows = draft.lines as any[];
    if (rows.some((row) => !row.itemId)) {
      await api.showError(this, new Error("ItemRequired"));
      return;
    }

    const lines = rows.map((line) => ({
      itemId: Number(line.itemId),
      count: Number(line.count),
      // a price of 0 is a real value, not a missing one
      unitPrice:
        line.unitPrice == null || line.unitPrice === ""
          ? null
          : Number(line.unitPrice),
    }));

    // The invoice goes to the recipient the dialog shows. The dialog is built
    // once and reused, so its select keeps the selection of the previous open
    // while the draft of this open starts out empty — reading the draft would
    // send recipient 0 and fail with RecipientNotFound.
    const recipientId = Number(
      (this.byId("recipient") as Select).getSelectedKey(),
    );

    // long-running task: creating an invoice renders its documents, so the
    // interface is blocked until it finishes
    this._saving = true;
    const busy = new BusyDialog({ title: this.i18nText("invoiceRunning") });
    busy.open();

    let failure: unknown = null;
    try {
      if (draft.invoiceId) {
        await api.createRevision(Number(draft.invoiceId), recipientId, lines);
      } else {
        await api.createInvoice(recipientId, lines);
      }

      this._dialog.close();
      (
        (this.byId("invoicesTable") as any).getBinding(
          "items",
        ) as ODataListBinding
      ).refresh();
    } catch (error) {
      failure = error;
    } finally {
      // close the busy dialog before any error message opens
      busy.close();
      this._saving = false;
    }

    if (failure) {
      await api.showError(this, failure);
    }
  }

  public onInvoiceDialogCancel(): void {
    this._dialog.close();
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
      // refresh the recipient select's list so the new row is included,
      // then select it in the parent invoice dialog, which stays open
      const select = this.byId("recipient") as Select;
      await (select.getBinding("items") as ODataListBinding).refresh();
      (this._dialog.getModel("invoiceDraft") as JSONModel).setProperty(
        "/recipientId",
        id,
      );
      dialog.close();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onQuickRecipientCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  public async onDownloadNormal(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openDocument(context.getProperty("Id") as number, false);
  }

  public async onDownloadCopy(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openDocument(context.getProperty("Id") as number, true);
  }

  private async onBulk(
    kind: "Normal" | "Copy",
    fileName: string,
  ): Promise<void> {
    try {
      // the half-year filter also narrows the bulk download
      const key = (this.byId("halfYearFilter") as Select).getSelectedKey();
      let year: number | undefined;
      let half: number | undefined;
      if (key) {
        const [yearPart, halfPart] = key.split("-").map(Number);
        year = yearPart;
        half = halfPart;
      }

      await api.bulkDownload(kind, fileName, year, half);
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public async onBulkNormal(): Promise<void> {
    await this.onBulk("Normal", "rechnungen.zip");
  }

  public async onBulkCopy(): Promise<void> {
    await this.onBulk("Copy", "kopien.zip");
  }

  private _saving = false;
  private _quickDialog: any;
  private _dialog: any;
}
