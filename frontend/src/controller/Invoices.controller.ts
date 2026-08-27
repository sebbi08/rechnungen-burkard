import type ResourceBundle from "sap/base/i18n/ResourceBundle";
import Input from "sap/m/Input";
import Select from "sap/m/Select";
import TextArea from "sap/m/TextArea";
import Item from "sap/ui/core/Item";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import JSONModel from "sap/ui/model/json/JSONModel";
import Context from "sap/ui/model/odata/v4/Context";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import ResourceModel from "sap/ui/model/resource/ResourceModel";
import * as api from "../model/api";
import BaseController from "./Base.controller";

export default class Invoices extends BaseController {
  public onInit(): void {
    // routed views run onInit before they are added to the App, so the
    // odata model is not visible via view.getModel() yet
    const view = this.getView()!;
    const model = this.getOwnerComponent()!.getModel("odata") as ODataModel;
    view.setBindingContext(model.createBindingContext("/") as Context, "odata");

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
      : [{ itemId: null, count: 1, unitPrice: null }];
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
        itemId: line.ItemId,
        count: line.Count,
        unitPrice: line.UnitPrice,
      }));
    } catch (error) {
      await api.showError(this, error);
      return [{ itemId: null, count: 1, unitPrice: null }];
    }
  }

  public onAddLine(): void {
    const model = this._dialog.getModel("invoiceDraft") as JSONModel;
    const lines = (model.getProperty("/lines") as any[]) ?? [];
    lines.push({ itemId: null, count: 1, unitPrice: null });
    model.setProperty("/lines", lines);
  }

  public async onInvoiceDialogSave(): Promise<void> {
    const model = this._dialog.getModel("invoiceDraft") as JSONModel;
    const draft = model.getData() as any;
    const lines = (draft.lines as any[]).map((line) => ({
      itemId: Number(line.itemId),
      count: Number(line.count),
      // a price of 0 is a real value, not a missing one
      unitPrice: line.unitPrice != null ? Number(line.unitPrice) : null,
    }));

    try {
      if (draft.invoiceId) {
        await api.createRevision(
          Number(draft.invoiceId),
          Number(draft.recipientId),
          lines,
        );
      } else {
        await api.createInvoice(Number(draft.recipientId), lines);
      }

      this._dialog.close();
      (
        (this.byId("invoicesTable") as any).getBinding(
          "items",
        ) as ODataListBinding
      ).refresh();
    } catch (error) {
      await api.showError(this, error);
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
    await this.download(
      event.getSource().getBindingContext("odata") as Context,
      false,
    );
  }

  public async onDownloadCopy(event: any): Promise<void> {
    await this.download(
      event.getSource().getBindingContext("odata") as Context,
      true,
    );
  }

  private async download(context: Context, isCopy: boolean): Promise<void> {
    try {
      const invoiceId = context.getProperty("Id") as number;
      const documents = await api.fetchDocuments(invoiceId);
      const match = documents.find(
        (d) => d.FileName.includes("Kopie") === isCopy,
      );
      if (match) {
        window.open(api.downloadUrl(match.Id), "_blank");
      }
    } catch (error) {
      await api.showError(this, error);
    }
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

  public formatDate(value: Date | string | null | undefined): string {
    if (value == null) {
      return "";
    }
    const date = value instanceof Date ? value : new Date(value);
    return date.toLocaleDateString("de-DE");
  }

  public formatInvoiceNumber(value: string | null | undefined): string {
    if (value == null) {
      return "";
    }
    const index = value.lastIndexOf("-R");
    return index >= 0 ? value.slice(0, index) : value;
  }

  public formatRevisionBadge(value: number | null | undefined): string {
    return value != null && value > 0 ? `R${value}` : "";
  }

  private _quickDialog: any;
  private _dialog: any;
}
