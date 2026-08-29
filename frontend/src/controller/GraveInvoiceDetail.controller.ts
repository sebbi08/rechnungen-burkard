import type { Button$PressEvent } from "sap/m/Button";
import type { Route$PatternMatchedEvent } from "sap/ui/core/routing/Route";
import * as api from "../model/api";
import { emptyRevisionDetail, type RevisionDetailView } from "../model/state";
import GraveEdit from "./GraveEdit.controller";

/** Every revision of one grave invoice, plus what makes it one: the grave, the
 *  half-year, the share, and the other recipients billed for the same period. */
export default class GraveInvoiceDetail extends GraveEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.router()
      .getRoute("graveInvoiceDetail")!
      // wrapped rather than passed directly: the handler is async and the
      // event expects a void return
      .attachPatternMatched((event) => void this.onRouteMatched(event));
  }

  private async onRouteMatched(event: Route$PatternMatchedEvent): Promise<void> {
    const args = event.getParameter("arguments") as {
      graveId: string;
      rootId: string;
    };
    this._graveId = Number(args.graveId);
    this._rootId = Number(args.rootId);
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    try {
      const chain = await this.loadChain(this._rootId);
      if (!chain.length) {
        this.setState<RevisionDetailView>("detail", emptyRevisionDetail());
        return;
      }

      const detail = this.buildRevisionDetail(chain);
      const latestInvoiceId = detail.latestInvoiceId!;
      const group = await api.fetchGraveInvoiceGroup(latestInvoiceId);
      const share = detail.sharePercent;

      detail.graveNumber = group.GraveNumber;
      detail.shareText =
        share != null ? `${share.toFixed(1).replace(".", ",")} %` : "";
      // the siblings of this half-year, minus the invoice being shown
      detail.others = group.Recipients.filter(
        (r) => r.InvoiceId !== latestInvoiceId,
      );
      this.setState<RevisionDetailView>("detail", detail);
    } catch (error) {
      api.showError(this, error);
    }
  }

  /** Back to the grave this invoice belongs to, not to the home screen. */
  public onNavBack(): void {
    this.router().navTo("graveDetail", { graveId: String(this._graveId) });
  }

  public onOpenSibling(event: Button$PressEvent): void {
    const context = event.getSource().getBindingContext("detail")!;
    this.router().navTo("graveInvoiceDetail", {
      graveId: String(this._graveId),
      rootId: String(context.getProperty("RootId")),
    });
  }

  public async onEditGraveInvoice(): Promise<void> {
    const invoiceId = this.state<RevisionDetailView>("detail").getProperty(
      "/latestInvoiceId",
    );
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

  public async onDownloadRevisionNormal(
    event: Button$PressEvent,
  ): Promise<void> {
    await this.downloadRevision(event, false);
  }

  public async onDownloadRevisionCopy(
    event: Button$PressEvent,
  ): Promise<void> {
    await this.downloadRevision(event, true);
  }

  private async downloadRevision(
    event: Button$PressEvent,
    isCopy: boolean,
  ): Promise<void> {
    const context = event.getSource().getBindingContext("detail")!;
    await this.openDocument(Number(context.getProperty("Id")), isCopy);
  }

  private _graveId = 0;
  private _rootId = 0;
}
