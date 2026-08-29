import type ResourceBundle from "sap/base/i18n/ResourceBundle";
import Router from "sap/m/routing/Router";
import Controller from "sap/ui/core/mvc/Controller";
import UIComponent from "sap/ui/core/UIComponent";
import TypedJSONModel from "sap/ui/model/json/TypedJSONModel";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import type ResourceModel from "sap/ui/model/resource/ResourceModel";
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
      this.odata().createBindingContext("/"),
      "odata",
    );
  }

  /**
   * Puts a view's state into a model the XML can bind to, so no handler has to
   * ask a control what it is showing. TypedJSONModel checks every path against
   * `T`, which turns a mistyped binding path into a compile error.
   */
  protected setState<T extends object>(name: string, data: T): TypedJSONModel<T> {
    const model = new TypedJSONModel<T>(data);
    this.getView()!.setModel(model, name);
    return model;
  }

  /** The model {@link setState} created, typed back to what it holds. */
  protected state<T extends object>(name: string): TypedJSONModel<T> {
    return this.getView()!.getModel(name) as TypedJSONModel<T>;
  }

  /**
   * Reads a list through the OData model — every read stays on the model
   * (D-002) — and hands back plain objects for a JSONModel. The detail pages
   * compute totals per revision, which a list binding cannot express.
   */
  protected async readList<T>(
    path: string,
    parameters: Record<string, unknown>,
  ): Promise<T[]> {
    // The OData controller caps $top at 200 (Program.cs, SetMaxTop). A single
    // requestContexts(0, N) with N above that is rejected outright, so page
    // through in that size rather than assuming everything fits in one shot.
    const PAGE_SIZE = 200;
    const binding = this.odata().bindList(
      path,
      undefined,
      undefined,
      undefined,
      parameters,
    );
    const rows: T[] = [];
    for (let offset = 0; ; offset += PAGE_SIZE) {
      const contexts = await binding.requestContexts(offset, PAGE_SIZE);
      rows.push(...contexts.map((context) => context.getObject() as T));
      if (contexts.length < PAGE_SIZE) {
        break;
      }
    }
    binding.destroy();
    return rows;
  }

  /**
   * Which recipient a dialog's select starts on.
   *
   * A select would fall back to its first item on its own, but that fallback is
   * the control's and UI5 does not write it into the bound model — a save would
   * then send recipient 0. Choosing the default here keeps the draft the one
   * thing that says who is meant. Newest first, the order the selects list them
   * in.
   */
  protected async newestRecipientId(): Promise<number | undefined> {
    const binding = this.odata().bindList(
      "/Recipients",
      undefined,
      undefined,
      undefined,
      { $orderby: "Id desc", $select: "Id" },
    );
    try {
      const contexts = await binding.requestContexts(0, 1);
      return contexts[0]?.getProperty("Id") as number | undefined;
    } finally {
      binding.destroy();
    }
  }

  protected i18nText(key: string): string {
    const bundle = (
      this.getView()!.getModel("i18n") as ResourceModel
    ).getResourceBundle() as ResourceBundle;
    return bundle.getText(key) ?? key;
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
      api.showError(this, error);
    }
  }
}
