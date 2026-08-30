import type { Button$PressEvent } from "sap/m/Button";
import type { Route$PatternMatchedEvent } from "sap/ui/core/routing/Route";
import * as api from "../model/api";
import { emptyRevisionDetail, type RevisionDetailView } from "../model/state";
import InvoiceEdit from "./InvoiceEdit.controller";

/** Every revision of one invoice, newest first. */
export default class InvoiceDetail extends InvoiceEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.router()
      .getRoute("invoiceDetail")!
      // wrapped rather than passed directly: the handler is async and the
      // event expects a void return
      .attachPatternMatched((event) => void this.onRouteMatched(event));
  }

  private async onRouteMatched(event: Route$PatternMatchedEvent): Promise<void> {
    const args = event.getParameter("arguments") as { rootId: string };
    this._rootId = Number(args.rootId);
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    try {
      const chain = await this.loadChain(this._rootId);
      this.setState<RevisionDetailView>(
        "detail",
        chain.length ? this.buildRevisionDetail(chain) : emptyRevisionDetail(),
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
    const model = this.state<RevisionDetailView>("detail");
    const invoiceId = model.getProperty("/latestInvoiceId");
    if (invoiceId == null) {
      return;
    }
    await this.openInvoiceDialog(invoiceId, model.getProperty("/recipientId"));
  }

  protected async afterInvoiceSaved(): Promise<void> {
    await this.refresh();
  }

  public async onDownloadRevisionNormal(event: Button$PressEvent): Promise<void> {
    await this.downloadRevision(event, false);
  }

  public async onDownloadRevisionCopy(event: Button$PressEvent): Promise<void> {
    await this.downloadRevision(event, true);
  }

  private async downloadRevision(event: Button$PressEvent, isCopy: boolean): Promise<void> {
    const context = event.getSource().getBindingContext("detail")!;
    await this.openDocument(Number(context.getProperty("Id")), isCopy);
  }

  private _rootId = 0;
}
