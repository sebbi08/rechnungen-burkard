import Router from "sap/m/routing/Router";
import Controller from "sap/ui/core/mvc/Controller";
import UIComponent from "sap/ui/core/UIComponent";
import Context from "sap/ui/model/odata/v4/Context";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import * as api from "../model/api";

export default abstract class BaseController extends Controller {
  public onNavBack(): void {
    this.router().navTo("home");
  }

  protected router(): Router {
    return UIComponent.getRouterFor(this) as Router;
  }

  protected odata(): ODataModel {
    return this.getOwnerComponent()!.getModel("odata") as ODataModel;
  }

  /**
   * Binds the view to the OData root context. Routed views run onInit before
   * they are added to the App, so view.getModel("odata") is not visible yet.
   */
  protected bindODataRoot(): void {
    this.getView()!.setBindingContext(
      this.odata().createBindingContext("/") as Context,
      "odata",
    );
  }

  /**
   * Reads a list through the OData model — every read stays on the model
   * (D-002) — and hands back plain objects for a JSONModel. The detail pages
   * compute totals per revision, which a list binding cannot express.
   */
  protected async readList(
    path: string,
    parameters: Record<string, unknown>,
  ): Promise<any[]> {
    const binding = this.odata().bindList(
      path,
      undefined,
      undefined,
      undefined,
      parameters as any,
    );
    const contexts = await binding.requestContexts(0, 500);
    const rows = contexts.map((context) => context.getObject());
    binding.destroy();
    return rows;
  }

  protected i18nText(key: string): string {
    return (this.getView()!.getModel("i18n") as any)
      .getResourceBundle()
      .getText(key);
  }

  public formatDate(value: Date | string | null | undefined): string {
    if (value == null) {
      return "";
    }
    const date = value instanceof Date ? value : new Date(value);
    return date.toLocaleDateString("de-DE");
  }

  /** The same locale formatDate uses, so amounts and dates read as one page. */
  public formatAmount(value: number | string | null | undefined): string {
    if (value == null || value === "") {
      return "";
    }
    return `${Number(value).toLocaleString("de-DE", {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })} €`;
  }

  /** "2026-0042-R1" -> "2026-0042": the chain, without the revision marker. */
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

  /** Opens an invoice's PDF in a new tab, where the browser displays it. */
  protected async openDocument(
    invoiceId: number,
    isCopy: boolean,
  ): Promise<void> {
    // The tab is opened before the documents are fetched: one opened after the
    // await no longer belongs to the click, and browsers block it as a popup.
    const tab = window.open("", "_blank");
    try {
      const documents = await api.fetchDocuments(invoiceId);
      const wanted = isCopy ? "Copy" : "Normal";
      const match = documents.find((d) => d.Kind === wanted);
      if (!match) {
        tab?.close();
        return;
      }

      if (tab) {
        tab.location.href = api.documentUrl(match.Id);
      } else {
        // a browser that refused the empty tab may still allow a real URL
        window.open(api.documentUrl(match.Id), "_blank");
      }
    } catch (error) {
      tab?.close();
      await api.showError(this, error);
    }
  }
}
