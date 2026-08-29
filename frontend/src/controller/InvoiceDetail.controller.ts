import JSONModel from "sap/ui/model/json/JSONModel";
import * as api from "../model/api";
import InvoiceEdit from "./InvoiceEdit.controller";

/** Every revision of one invoice, newest first. */
export default class InvoiceDetail extends InvoiceEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.router()
      .getRoute("invoiceDetail")!
      .attachPatternMatched(this.onRouteMatched, this);
  }

  private async onRouteMatched(event: any): Promise<void> {
    this._rootId = Number(event.getParameter("arguments").rootId);
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    try {
      const chain = await this.loadChain(this._rootId);
      this.getView()!.setModel(
        chain.length ? this.buildRevisionModel(chain) : new JSONModel({ revisions: [] }),
        "detail",
      );
    } catch (error) {
      api.showError(this, error);
    }
  }

  /** Back to the list this page was opened from, not to the home screen. */
  public onNavBack(): void {
    this.router().navTo("invoices");
  }

  public async onEditInvoice(): Promise<void> {
    const model = this.getView()!.getModel("detail") as JSONModel;
    const invoiceId = model.getProperty("/latestInvoiceId") as number | null;
    if (invoiceId == null) {
      return;
    }
    await this.openInvoiceDialog(
      invoiceId,
      model.getProperty("/recipientId") as number,
    );
  }

  protected async afterInvoiceSaved(): Promise<void> {
    await this.refresh();
  }

  public async onDownloadRevisionNormal(event: any): Promise<void> {
    await this.downloadRevision(event, false);
  }

  public async onDownloadRevisionCopy(event: any): Promise<void> {
    await this.downloadRevision(event, true);
  }

  private async downloadRevision(event: any, isCopy: boolean): Promise<void> {
    const context = event.getSource().getBindingContext("detail");
    await this.openDocument(Number(context.getProperty("Id")), isCopy);
  }

  private _rootId = 0;
}
