import type ResourceBundle from "sap/base/i18n/ResourceBundle";
import ComboBox from "sap/m/ComboBox";
import MessageBox from "sap/m/MessageBox";
import Router from "sap/m/routing/Router";
import Controller from "sap/ui/core/mvc/Controller";
import UIComponent from "sap/ui/core/UIComponent";
import TypedJSONModel from "sap/ui/model/json/TypedJSONModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
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
    this.getView()!.setBindingContext(this.odata().createBindingContext("/"), "odata");
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
  protected async readList<T>(path: string, parameters: Record<string, unknown>): Promise<T[]> {
    // The OData controller caps $top at 200 (Program.cs, SetMaxTop). A single
    // requestContexts(0, N) with N above that is rejected outright, so page
    // through in that size rather than assuming everything fits in one shot.
    const PAGE_SIZE = 200;
    const binding = this.odata().bindList(path, undefined, undefined, undefined, parameters);
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
   * Re-reads a recipient ComboBox's list, waits for it, and answers which
   * recipient a fresh draft should start on: the first row, which is the newest
   * because the list is ordered that way.
   *
   * Both dialogs are built once and reused, so without the refresh the list is
   * the one loaded on their first open and a recipient created since is missing
   * from it. The default is taken from this same list rather than from a read of
   * its own: two reads can disagree if a recipient is created between them, and
   * the draft would then name someone the ComboBox cannot show.
   *
   * A ComboBox selects nothing on its own, so this is the only thing that fills
   * the field. It reaches a list binding rather than view state, so it takes a
   * control id: no binding syntax expresses "reload now, and tell me when".
   */
  protected async refreshRecipients(comboBoxId: string): Promise<number | undefined> {
    const binding = (this.byId(comboBoxId) as ComboBox).getBinding("items") as
      ODataListBinding | undefined;
    if (!binding) {
      return undefined;
    }
    await binding.requestRefresh();
    const contexts = await binding.requestContexts(0, 1);
    return contexts[0]?.getProperty("Id") as number | undefined;
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

  /**
   * The first line of a recipient's address, which the recipient ComboBoxes
   * show as their secondary value. An address is stored as typed, over as many
   * lines as the user wrote, and one line is all a suggestion row has space for.
   */
  public formatAddressLine(value: string | null | undefined): string {
    return (value ?? "").split("\n")[0].trim();
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

  /** An amount, or a dash where there is none to show. An empty cell reads as
   *  "loading"; a dash says the figure does not exist — a grave without
   *  recipients has no billed price. */
  public formatAmountOrDash(value: number | string | null | undefined): string {
    return value == null || value === "" ? "–" : this.formatAmount(value);
  }

  /** A boolean column, spelled out rather than shown as a tick: the tables it
   *  appears in are read aloud by screen readers cell by cell. */
  public formatYesNo(value: boolean | null | undefined): string {
    return this.i18nText(value ? "yes" : "no");
  }

  /** "eingegeben: 47,80 €" — the price as typed, next to the one that is
   *  billed. The pattern carries the label so it stays translatable. */
  public formatEnteredPrice(
    pattern: string | null | undefined,
    value: number | string | null | undefined,
  ): string {
    if (value == null || value === "") {
      return "";
    }
    return (pattern ?? "{0}").replace("{0}", this.formatAmount(value));
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

  /**
   * The payment state of one invoice, as the lists and detail pages read it.
   * `PaidOn` holds the day the payment was recorded, but no screen shows it
   * (spec decision 3): a set date is "paid" and null is "unpaid", nothing more.
   *
   * "unbezahlt" rather than "offen": the grave overview already says "offen"
   * for a half-year with no invoice, and the two columns sit next to each other.
   */
  public paidStatusText(paidOn: string | null | undefined): string {
    return this.i18nText(paidOn ? "statusPaid" : "statusUnpaid");
  }

  /**
   * Warning, not Error, for an unpaid invoice: one that was just written is the
   * normal case, unlike a grave whose half-year has no invoice at all.
   */
  public paidStatusState(paidOn: string | null | undefined): string {
    return paidOn ? "Success" : "Warning";
  }

  /**
   * What a screen reader says about the payment status. Without it,
   * state="Warning" makes an unpaid row's status announced with a "Warnung"
   * ("warning") prefix, which an unpaid invoice is not — same reasoning as
   * Graves.controller's statusAnnouncement for the invoicing-status column.
   */
  public paidStatusAnnouncement(paidOn: string | null | undefined): string {
    return this.i18nText(paidOn ? "statusPaidAnnouncement" : "statusUnpaidAnnouncement");
  }

  public paidActionIcon(paidOn: string | null | undefined): string {
    return paidOn ? "sap-icon://decline" : "sap-icon://money-bills";
  }

  public paidActionTooltip(paidOn: string | null | undefined): string {
    return this.i18nText(paidOn ? "actionMarkUnpaid" : "actionMarkPaid");
  }

  /** A paid invoice cannot be revised, so its edit button is dead. The backend
   *  refuses either way; this only keeps a dialog from opening on a save that
   *  cannot succeed. */
  public paidEditEnabled(paidOn: string | null | undefined): boolean {
    return !paidOn;
  }

  /** What the edit button says when it is dead: a paid invoice cannot be
   *  revised, so the tooltip names the reason rather than the action. */
  public paidEditTooltip(paidOn: string | null | undefined): string {
    return this.i18nText(paidOn ? "editBlockedPaid" : "actionEdit");
  }

  /**
   * Marks an unpaid invoice paid straight away, or asks before taking an
   * existing mark back — unmarking discards the recorded day, which nothing in
   * the UI could show again.
   *
   * Answers whether anything changed, so each caller refreshes its own model
   * (a list binding, a JSON model, a whole detail page) rather than this
   * guessing which one it is in.
   */
  protected async togglePaid(invoiceId: number, paidOn: string | null): Promise<boolean> {
    if (paidOn && !(await this.confirmUnmark())) {
      return false;
    }

    try {
      await api.setInvoicePaid(invoiceId, !paidOn);
      return true;
    } catch (error) {
      api.showError(this, error);
      return false;
    }
  }

  /** `MessageBox.confirm` reports the pressed action through a callback; this
   *  turns it into something the caller can await. */
  private confirmUnmark(): Promise<boolean> {
    return new Promise((resolve) => {
      MessageBox.confirm(this.i18nText("confirmMarkUnpaid"), {
        onClose: (action: string | null) => resolve(action === MessageBox.Action.OK),
      });
    });
  }

  /** Opens an invoice's PDF in a new tab, where the browser displays it. */
  protected async openDocument(invoiceId: number, isCopy: boolean): Promise<void> {
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
