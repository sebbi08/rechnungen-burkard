import type ResourceBundle from "sap/base/i18n/ResourceBundle";
import Select from "sap/m/Select";
import Item from "sap/ui/core/Item";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import Context from "sap/ui/model/odata/v4/Context";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import ResourceModel from "sap/ui/model/resource/ResourceModel";
import * as api from "../model/api";
import InvoiceEdit from "./InvoiceEdit.controller";

export default class Invoices extends InvoiceEdit {
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
    await this.openInvoiceDialog(null, null);
  }

  public async onEditInvoice(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openInvoiceDialog(
      context.getProperty("Id") as number,
      context.getProperty("RecipientId") as number,
    );
  }

  protected afterInvoiceSaved(): void {
    (
      (this.byId("invoicesTable") as any).getBinding("items") as ODataListBinding
    ).refresh();
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
}
