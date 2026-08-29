import Context from "sap/ui/model/odata/v4/Context";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import InvoiceEdit from "./InvoiceEdit.controller";

export default class Invoices extends InvoiceEdit {
  public onInit(): void {
    this.bindODataRoot();
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

  /**
   * Narrows the list through the backend's `search` custom query option, which
   * matches the invoice number, recipient, date and total. An empty term is a
   * match-everything on the backend, so clearing the field restores the list
   * without special-casing it here.
   */
  public onSearchInvoices(event: any): void {
    const term = (event.getParameter("query") as string) ?? "";
    (
      (this.byId("invoicesTable") as any).getBinding("items") as ODataListBinding
    ).changeParameters({ search: term });
  }

  public onShowDetails(event: any): void {
    const context = event.getSource().getBindingContext("odata") as Context;
    this.router().navTo("invoiceDetail", {
      rootId: String(context.getProperty("RootId")),
    });
  }
}
