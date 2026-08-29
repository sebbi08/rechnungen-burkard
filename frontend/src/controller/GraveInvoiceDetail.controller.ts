import JSONModel from "sap/ui/model/json/JSONModel";
import * as api from "../model/api";
import GraveEdit from "./GraveEdit.controller";

/** Every revision of one grave invoice, plus what makes it one: the grave, the
 *  half-year, the share, and the other recipients billed for the same period. */
export default class GraveInvoiceDetail extends GraveEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.router()
      .getRoute("graveInvoiceDetail")!
      .attachPatternMatched(this.onRouteMatched, this);
  }

  private async onRouteMatched(event: any): Promise<void> {
    const args = event.getParameter("arguments");
    this._graveId = Number(args.graveId);
    this._rootId = Number(args.rootId);
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    try {
      const chain = await this.loadChain(this._rootId);
      if (!chain.length) {
        this.getView()!.setModel(
          new JSONModel({ revisions: [], others: [] }),
          "detail",
        );
        return;
      }

      const model = this.buildRevisionModel(chain);
      const latestInvoiceId = model.getProperty("/latestInvoiceId") as number;
      const group = await api.fetchGraveInvoiceGroup(latestInvoiceId);
      const share = model.getProperty("/sharePercent") as number | null;

      model.setProperty("/graveNumber", group.GraveNumber);
      model.setProperty(
        "/shareText",
        share != null ? `${share.toFixed(1).replace(".", ",")} %` : "",
      );
      // the siblings of this half-year, minus the invoice being shown
      model.setProperty(
        "/others",
        group.Recipients.filter((r) => r.InvoiceId !== latestInvoiceId),
      );
      this.getView()!.setModel(model, "detail");
    } catch (error) {
      await api.showError(this, error);
    }
  }

  /** Back to the grave this invoice belongs to, not to the home screen. */
  public onNavBack(): void {
    this.router().navTo("graveDetail", { graveId: String(this._graveId) });
  }

  public onOpenSibling(event: any): void {
    const context = event.getSource().getBindingContext("detail");
    this.router().navTo("graveInvoiceDetail", {
      graveId: String(this._graveId),
      rootId: String(context.getProperty("InvoiceId")),
    });
  }

  public async onEditGraveInvoice(): Promise<void> {
    const invoiceId = (this.getView()!.getModel("detail") as JSONModel).getProperty(
      "/latestInvoiceId",
    ) as number | null;
    if (invoiceId == null) {
      return;
    }
    await this.openGraveInvoiceDialog(invoiceId);
  }

  /** Both dialogs answer here; only the grave-invoice one is reachable from
   *  this page, but the hook is the same. */
  protected async refreshAfterSave(): Promise<void> {
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

  private _graveId = 0;
  private _rootId = 0;
}
