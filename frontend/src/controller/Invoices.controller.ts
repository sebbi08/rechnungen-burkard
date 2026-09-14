import type { Button$PressEvent } from "sap/m/Button";
import type { SearchField$SearchEvent } from "sap/m/SearchField";
import Table from "sap/m/Table";
import Context from "sap/ui/model/odata/v4/Context";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import type { InvoicesView } from "../model/state";
import InvoiceEdit from "./InvoiceEdit.controller";

export default class Invoices extends InvoiceEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.setState<InvoicesView>("view", { paymentFilter: "all" });
  }

  public async onCreateInvoice(): Promise<void> {
    await this.openInvoiceDialog(null, null);
  }

  public async onEditInvoice(event: Button$PressEvent): Promise<void> {
    const context = this.liveRow(event);
    if (!context) {
      return;
    }
    await this.openInvoiceDialog(
      context.getProperty("Id") as number,
      context.getProperty("RecipientId") as number,
    );
  }

  protected afterInvoiceSaved(): void {
    this.odata().refresh();
  }

  public async onDownloadNormal(event: Button$PressEvent): Promise<void> {
    const context = this.liveRow(event);
    if (context) {
      await this.openDocument(context.getProperty("Id") as number, false);
    }
  }

  public async onDownloadCopy(event: Button$PressEvent): Promise<void> {
    const context = this.liveRow(event);
    if (context) {
      await this.openDocument(context.getProperty("Id") as number, true);
    }
  }

  /**
   * Narrows the list through the backend's `search` custom query option, which
   * matches the invoice number, recipient, date and total. An empty term is a
   * match-everything on the backend, so clearing the field restores the list
   * without special-casing it here.
   */
  public onSearchInvoices(event: SearchField$SearchEvent): void {
    const term = event.getParameter("query") ?? "";
    // `search` is a custom query option on the list binding rather than view
    // state, and no binding syntax expresses it, so this reaches the control.
    const binding = (this.byId("invoicesTable") as Table).getBinding("items") as ODataListBinding;
    binding.changeParameters({ search: term });
  }

  public onShowDetails(event: Button$PressEvent): void {
    const context = this.liveRow(event);
    if (context) {
      this.router().navTo("invoiceDetail", {
        rootId: String(context.getProperty("RootId")),
      });
    }
  }

  /**
   * The row an action belongs to, or null when its data has gone.
   *
   * Searching changes parameters on the list binding, and until the response
   * lands the old rows are still on screen with their contexts already
   * emptied. A click in that window read `undefined` and navigated to
   * `#/invoices/undefined`, which rendered a blank page nobody could act on —
   * and did the same for a download or an edit. Doing nothing leaves the row
   * one click away once the list has settled.
   */
  private liveRow(event: Button$PressEvent): Context | null {
    const context = event.getSource().getBindingContext("odata") as Context | null;
    return context?.getProperty("Id") == null ? null : context;
  }

  public async onTogglePaid(event: Button$PressEvent): Promise<void> {
    const context = this.liveRow(event);
    if (!context) {
      return;
    }
    const changed = await this.togglePaid(
      context.getProperty("Id") as number,
      context.getProperty("PaidOn") as string | null,
    );
    if (changed) {
      this.odata().refresh();
    }
  }

  /**
   * The payment filter and the static list filter are one `$filter`, so the
   * clause is composed rather than set: `changeParameters` is also what the
   * search field uses, and `search` is a separate query option, so a filtered
   * search stays filtered.
   */
  public onPaymentFilterChange(): void {
    const key = this.state<InvoicesView>("view").getProperty("/paymentFilter");
    const payment =
      key === "unpaid" ? " and PaidOn eq null" : key === "paid" ? " and PaidOn ne null" : "";
    const binding = (this.byId("invoicesTable") as Table).getBinding("items") as ODataListBinding;
    binding.changeParameters({
      $filter: `GraveId eq null and IsLatestRevision eq true${payment}`,
    });
  }
}
