import type { Button$PressEvent } from "sap/m/Button";
import BusyDialog from "sap/m/BusyDialog";
import type { ComboBox$SelectionChangeEvent } from "sap/m/ComboBox";
import Dialog from "sap/m/Dialog";
import MessageBox from "sap/m/MessageBox";
import TypedJSONModel from "sap/ui/model/json/TypedJSONModel";
import * as api from "../model/api";
import type { DraftLine, GraveDraft, GraveInvoiceDraft, QuickRecipientDraft } from "../model/state";
import InvoiceRevisions from "./InvoiceRevisions.controller";

/**
 * Owns the dialogs that edit a grave and its invoices. The grave list, the grave
 * detail page and the grave-invoice detail page all open one of them.
 */
export default abstract class GraveEdit extends InvoiceRevisions {
  /** Runs after a save in either dialog so the screen can reload what it shows.
   *  One hook for both, because every screen answers it the same way. */
  protected abstract refreshAfterSave(): void | Promise<void>;

  protected async openGraveDialog(graveId: number | null): Promise<void> {
    if (!this._graveDialog) {
      this._graveDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.GraveDialog",
      })) as Dialog;
    }

    // the link field starts on whichever recipient this refreshed list shows first
    const newest = await this.refreshRecipients("linkRecipient");

    // everything the dialog collects lives in this draft until it is saved, so
    // items and recipients can be added before the grave itself exists
    this._graveDialog.setModel(
      new TypedJSONModel<GraveDraft>(
        graveId != null
          ? await this.loadGraveDraft(graveId)
          : {
              Id: null,
              GraveNumber: "",
              StartDate: "",
              EndDate: "",
              Items: [],
              Recipients: [],
              // the link row is only offered while creating
              linkRecipientId: String(newest ?? ""),
              linkRecipientName: "",
            },
      ),
      "graveDraft",
    );

    this._graveDialog.open();
  }

  private async loadGraveDraft(graveId: number): Promise<GraveDraft> {
    const details = await api.fetchGraveDetails(graveId);
    return {
      Id: details.Id,
      GraveNumber: details.GraveNumber,
      StartDate: details.StartDate,
      EndDate: details.EndDate,
      Items: details.Items.map((link) => ({
        // the ComboBox matches its keys as strings
        itemId: String(link.Item.Id),
        itemNumber: link.Item.Number,
        description: link.Item.Description,
        count: link.Count,
        // stored as it was typed — for a VAT-included line that is the gross
        unitPrice: link.UnitPrice,
        defaultPrice: link.Item.DefaultPrice,
        priceIncludesVat: link.PriceIncludesVat,
      })),
      Recipients: details.Recipients.map((link) => ({
        recipientId: String(link.Recipient.Id),
        name: link.Recipient.Name,
      })),
      // an existing grave's recipients are fixed, so the link row stays hidden
      linkRecipientId: "",
      linkRecipientName: "",
    };
  }

  public onGraveItemSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes the key itself; fill in what the row shows alongside it
    const itemContext = event.getParameter("selectedItem")?.getBindingContext("odata");
    const rowContext = event.getSource().getBindingContext("graveDraft");
    if (!itemContext || !rowContext) {
      return;
    }

    const model = this.graveDraft();
    const row = Number(rowContext.getPath().split("/").pop());
    model.setProperty(`/Items/${row}/itemNumber`, itemContext.getProperty("Number") as string);
    model.setProperty(
      `/Items/${row}/description`,
      itemContext.getProperty("Description") as string,
    );
    // as in the invoice dialog: the item's default price is pre-filled and stays
    // overridable; an item without one leaves the field empty, where it falls
    // back to the item's price at invoicing time
    const defaultPrice = itemContext.getProperty("DefaultPrice") as number | null;
    model.setProperty(`/Items/${row}/defaultPrice`, defaultPrice);
    model.setProperty(
      `/Items/${row}/unitPrice`,
      defaultPrice != null ? Number(defaultPrice) : null,
    );
  }

  public onAddItemLink(): void {
    const model = this.graveDraft();
    const rows = model.getProperty("/Items");
    rows.push(GraveEdit.emptyLine());
    model.setProperty("/Items", rows);
  }

  public onLinkRecipientSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes its key into the draft; keep the name it shows too, so
    // the added row can be labelled without looking the recipient up again
    const selected = event.getParameter("selectedItem");
    this.graveDraft().setProperty("/linkRecipientName", selected?.getText() ?? "");
  }

  public async onAddRecipientLink(): Promise<void> {
    const model = this.graveDraft();
    const recipientId = model.getProperty("/linkRecipientId");
    if (!recipientId) {
      return;
    }

    const rows = model.getProperty("/Recipients");
    if (rows.some((row) => row.recipientId === recipientId)) {
      api.showError(this, new Error("GraveRecipientLinkExists"));
      return;
    }

    rows.push({
      recipientId,
      name: model.getProperty("/linkRecipientName") || (await this.recipientName(recipientId)),
    });
    model.setProperty("/Recipients", rows);
  }

  /** The name of a recipient the field shows but no selection event named yet. */
  private async recipientName(recipientId: string): Promise<string> {
    const context = this.odata().bindContext(`/Recipients(${recipientId})`).getBoundContext();
    const row = (await context.requestObject()) as { Name: string };
    return row.Name;
  }

  public onDeleteItemLink(event: Button$PressEvent): void {
    const model = this.graveDraft();
    const rows = model.getProperty("/Items");
    rows.splice(GraveEdit.rowIndex(event, "graveDraft"), 1);
    model.setProperty("/Items", rows);
  }

  public onDeleteRecipientLink(event: Button$PressEvent): void {
    const model = this.graveDraft();
    const rows = model.getProperty("/Recipients");
    rows.splice(GraveEdit.rowIndex(event, "graveDraft"), 1);
    model.setProperty("/Recipients", rows);
  }

  /** The row's binding path ends in its index, e.g. "/Items/2". */
  private static rowIndex(event: Button$PressEvent, model: string): number {
    const context = event.getSource().getBindingContext(model)!;
    return Number(context.getPath().split("/").pop());
  }

  private static emptyLine(): DraftLine {
    return {
      itemId: "",
      itemNumber: "",
      description: "",
      count: 1,
      unitPrice: null,
      defaultPrice: null,
      priceIncludesVat: false,
    };
  }

  public async onGraveDialogSave(): Promise<void> {
    const draft = this.graveDraft().getData();
    if (draft.Items.some((row) => !row.itemId)) {
      api.showError(this, new Error("ItemRequired"));
      return;
    }

    if (!(await this.confirmBilledPrices(draft))) {
      return;
    }

    try {
      await api.saveGrave({
        graveId: draft.Id != null ? Number(draft.Id) : null,
        graveNumber: draft.GraveNumber ?? "",
        startDate: draft.StartDate ?? "",
        endDate: draft.EndDate ?? "",
        items: draft.Items.map((row) => ({
          itemId: Number(row.itemId),
          count: Number(row.count),
          // a price of 0 is a real value, not a missing one
          unitPrice: row.unitPrice == null || row.unitPrice === "" ? null : Number(row.unitPrice),
          priceIncludesVat: row.priceIncludesVat,
        })),
        recipientIds: draft.Recipients.map((row) => Number(row.recipientId)),
      });

      this._graveDialog.close();
      await this.refreshAfterSave();
    } catch (error) {
      api.showError(this, error);
    }
  }

  /** Warns when the grave will not be billed at the prices that were typed, and
   *  waits for the user to accept it.
   *
   *  Two rules can move a price: converting a VAT-included price to a net
   *  rounds it, and the split raises unit prices by whole cents until each VAT
   *  rate's subtotal divides across the recipients and the shares still cover
   *  the amount. Neither is visible while typing, and the first time anyone
   *  noticed was on a finished invoice — hence a confirmation here rather than
   *  a surprise later.
   *
   *  Answers true when there is nothing to warn about, so the caller can treat
   *  it as "go ahead". A draft with no recipients or no items has no billed
   *  price yet and warns about nothing; a preview that fails does not block a
   *  save, since refusing to save because a warning could not be computed would
   *  be worse than saving without it. */
  private async confirmBilledPrices(draft: GraveDraft): Promise<boolean> {
    const recipients = draft.Recipients.length;
    if (recipients === 0 || draft.Items.length === 0) {
      return true;
    }

    let billed: api.GraveBillingPreviewLine[];
    try {
      billed = await api.previewGraveBilling(
        recipients,
        draft.Items.map((row) => ({
          ItemId: Number(row.itemId),
          Count: Number(row.count),
          UnitPrice: row.unitPrice == null || row.unitPrice === "" ? null : Number(row.unitPrice),
          PriceIncludesVat: row.priceIncludesVat,
        })),
      );
    } catch {
      return true;
    }

    const raised = billed.filter((line) => Number(line.BilledPrice) !== Number(line.EnteredPrice));
    if (raised.length === 0) {
      return true;
    }

    const lines = raised.map((line) =>
      this.i18nText("confirmBilledPriceLine")
        .replace("{0}", line.Description)
        .replace("{1}", this.formatAmount(line.EnteredPrice))
        .replace("{2}", this.formatAmount(line.BilledPrice)),
    );
    const message = [
      this.i18nText("confirmBilledPriceIntro"),
      "",
      ...lines,
      "",
      this.i18nText("confirmBilledPriceWhy"),
    ].join("\n");

    return new Promise((resolve) => {
      MessageBox.confirm(message, {
        title: this.i18nText("confirmBilledPriceTitle"),
        onClose: (action: string | null) => resolve(action === MessageBox.Action.OK),
      });
    });
  }

  public onGraveDialogCancel(): void {
    this._graveDialog.close();
  }

  public async onNewRecipient(): Promise<void> {
    if (!this._quickDialog) {
      this._quickDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.RecipientQuickCreate",
      })) as Dialog;
    }
    this._quickDialog.setModel(
      new TypedJSONModel<QuickRecipientDraft>({ name: "", address: "" }),
      "quickRecipientDraft",
    );
    this._quickDialog.open();
  }

  public async onQuickRecipientSave(): Promise<void> {
    const quick = (
      this._quickDialog.getModel("quickRecipientDraft") as TypedJSONModel<QuickRecipientDraft>
    ).getData();
    try {
      const id = await api.createRecipient({
        Name: quick.name,
        Address: quick.address,
      });
      // refresh the link ComboBox's list so the new row is included, then select
      // it; the parent grave dialog stays open
      await this.refreshRecipients("linkRecipient");
      const model = this.graveDraft();
      model.setProperty("/linkRecipientId", String(id));
      model.setProperty("/linkRecipientName", quick.name);
      this._quickDialog.close();
    } catch (error) {
      api.showError(this, error);
    }
  }

  public onQuickRecipientCancel(): void {
    this._quickDialog.close();
  }

  /**
   * Opens the dialog on the grave's lines as they were invoiced for that
   * half-year — read back from the invoices, not from the grave's current items,
   * so a grave edited since then cannot leak into a revision of this period.
   */
  protected async openGraveInvoiceDialog(invoiceId: number): Promise<void> {
    let group: api.GraveInvoiceGroup;
    try {
      group = await api.fetchGraveInvoiceGroup(invoiceId);
    } catch (error) {
      api.showError(this, error);
      return;
    }

    // One edit revises every recipient of the half-year, so one paid recipient
    // blocks all of them. The group knows who they are; saying so here beats
    // letting the user fill a dialog the backend will refuse.
    const paid = group.Recipients.filter((recipient) => recipient.Paid);
    if (paid.length) {
      MessageBox.error(
        this.i18nText("errorGraveInvoicePaidNames").replace(
          "{0}",
          paid.map((recipient) => recipient.Name).join(", "),
        ),
      );
      return;
    }

    if (!this._graveInvoiceDialog) {
      this._graveInvoiceDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.GraveInvoiceDialog",
      })) as Dialog;
    }

    const warning = this.i18nText("graveInvoiceEditWarning")
      .replace("{0}", api.halfYearText(group.PeriodYear, group.PeriodHalf))
      .replace("{1}", group.Recipients.map((r) => r.Name).join(", "));

    let rates: Map<number, number>;
    try {
      rates = await this.loadItemVatRates();
    } catch (error) {
      api.showError(this, error);
      return;
    }

    this._graveInvoiceDialog.setModel(
      new TypedJSONModel<GraveInvoiceDraft>({
        invoiceId,
        warning,
        lines: group.Lines.map((line) => {
          const vatRate = rates.get(Number(line.ItemId)) ?? 0;
          return {
            // the ComboBox matches its keys as strings
            itemId: String(line.ItemId),
            itemNumber: line.ItemNumber,
            description: line.Description,
            count: Number(line.Count),
            // No rounding for a net price: a pre-GraveUnitPrice-migration line is
            // reconstructed by GraveInvoiceSplitter.Combine at 3-decimal precision
            // on purpose, to reproduce its historical net cents exactly. Rounding
            // here would resubmit a different price for every line the user never
            // touched. A VAT-included line is the one case that does convert, and
            // its round trip is exact.
            unitPrice: api.priceFieldValue(
              Number(line.UnitPrice),
              Boolean(line.PriceIncludesVat),
              vatRate,
            ),
            defaultPrice: null,
            priceIncludesVat: Boolean(line.PriceIncludesVat),
          };
        }),
      }),
      "graveInvoiceDraft",
    );
    this._graveInvoiceDialog.open();
  }

  public onGraveInvoiceItemSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes the key itself; fill in what the row shows alongside it
    const itemContext = event.getParameter("selectedItem")?.getBindingContext("odata");
    const rowContext = event.getSource().getBindingContext("graveInvoiceDraft");
    if (!itemContext || !rowContext) {
      return;
    }

    const model = this.graveInvoiceDraft();
    const row = Number(rowContext.getPath().split("/").pop());
    model.setProperty(`/lines/${row}/itemNumber`, itemContext.getProperty("Number") as string);
    model.setProperty(
      `/lines/${row}/description`,
      itemContext.getProperty("Description") as string,
    );
    const defaultPrice = itemContext.getProperty("DefaultPrice") as number | null;
    model.setProperty(`/lines/${row}/defaultPrice`, defaultPrice);
    model.setProperty(
      `/lines/${row}/unitPrice`,
      defaultPrice != null ? Number(defaultPrice) : null,
    );
  }

  public onAddGraveInvoiceLine(): void {
    const model = this.graveInvoiceDraft();
    const rows = model.getProperty("/lines");
    rows.push(GraveEdit.emptyLine());
    model.setProperty("/lines", rows);
  }

  public onDeleteGraveInvoiceLine(event: Button$PressEvent): void {
    const model = this.graveInvoiceDraft();
    const rows = model.getProperty("/lines");
    rows.splice(GraveEdit.rowIndex(event, "graveInvoiceDraft"), 1);
    model.setProperty("/lines", rows);
  }

  public async onGraveInvoiceDialogSave(): Promise<void> {
    // a second press would revise the group twice; the busy dialog blocks the
    // interface, this guard covers the moment before it is on screen
    if (this._savingGraveInvoice) {
      return;
    }

    const draft = this.graveInvoiceDraft().getData();
    if (draft.lines.some((row) => !row.itemId)) {
      api.showError(this, new Error("ItemRequired"));
      return;
    }

    const lines = draft.lines.map((row) => ({
      itemId: Number(row.itemId),
      count: Number(row.count),
      // a price of 0 is a real value, not a missing one
      unitPrice: row.unitPrice == null || row.unitPrice === "" ? null : Number(row.unitPrice),
      priceIncludesVat: row.priceIncludesVat,
    }));

    // long-running task: a revision renders both documents for every recipient
    this._savingGraveInvoice = true;
    const busy = new BusyDialog({ title: this.i18nText("invoiceRunning") });
    busy.open();

    let failure: unknown = null;
    try {
      await api.createGraveInvoiceRevision(Number(draft.invoiceId), lines);
      this._graveInvoiceDialog.close();
      await this.refreshAfterSave();
    } catch (error) {
      failure = error;
    } finally {
      // close the busy dialog before any error message opens
      busy.close();
      this._savingGraveInvoice = false;
    }

    if (failure) {
      api.showError(this, failure);
    }
  }

  public onGraveInvoiceDialogCancel(): void {
    this._graveInvoiceDialog.close();
  }

  /** Both drafts live on their dialog rather than the view. */
  private graveDraft(): TypedJSONModel<GraveDraft> {
    return this._graveDialog.getModel("graveDraft") as TypedJSONModel<GraveDraft>;
  }

  private graveInvoiceDraft(): TypedJSONModel<GraveInvoiceDraft> {
    return this._graveInvoiceDialog.getModel(
      "graveInvoiceDraft",
    ) as TypedJSONModel<GraveInvoiceDraft>;
  }

  private _savingGraveInvoice = false;
  private _graveInvoiceDialog!: Dialog;

  protected _graveDialog!: Dialog;
  private _quickDialog!: Dialog;
}
