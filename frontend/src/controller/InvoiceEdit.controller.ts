import type { ComboBox$SelectionChangeEvent } from "sap/m/ComboBox";
import BusyDialog from "sap/m/BusyDialog";
import Dialog from "sap/m/Dialog";
import TypedJSONModel from "sap/ui/model/json/TypedJSONModel";
import * as api from "../model/api";
import type { DraftLine, InvoiceDraft, QuickRecipientDraft } from "../model/state";
import InvoiceRevisions from "./InvoiceRevisions.controller";

/**
 * Owns the invoice dialog. Both the invoice list and the invoice detail page
 * open it, so it lives here rather than being copied into each of them.
 */
export default abstract class InvoiceEdit extends InvoiceRevisions {
  /** Runs after a save so the screen can refresh whatever it is showing. */
  protected abstract afterInvoiceSaved(): void | Promise<void>;

  protected async openInvoiceDialog(
    invoiceId: number | null,
    recipientId: number | null,
  ): Promise<void> {
    if (!this._invoiceDialog) {
      this._invoiceDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.InvoiceDialog",
      })) as Dialog;
    }

    // the draft starts on whichever recipient this refreshed list shows first
    const newest = await this.refreshRecipients("recipient");

    let lines: DraftLine[];
    if (invoiceId != null) {
      let rates: Map<number, number>;
      try {
        rates = await this.loadItemVatRates();
      } catch (error) {
        api.showError(this, error);
        return;
      }
      lines = await this.loadLines(invoiceId, rates);
    } else {
      lines = [InvoiceEdit.emptyLine()];
    }
    this._invoiceDialog.setModel(
      new TypedJSONModel<InvoiceDraft>({
        // the ComboBox matches its keys as strings
        recipientId: String(recipientId ?? newest ?? ""),
        invoiceId,
        lines,
      }),
      "invoiceDraft",
    );
    this._invoiceDialog.open();
  }

  private async loadLines(invoiceId: number, rates: Map<number, number>): Promise<DraftLine[]> {
    try {
      const response = await fetch(`/odata/Invoices(${invoiceId})/Lines`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      const body = (await response.json()) as {
        value: api.InvoiceLineRow[];
      };
      return body.value.map((line) => {
        const vatRate = line.ItemId != null ? (rates.get(Number(line.ItemId)) ?? 0) : 0;
        return {
          // the ComboBox matches its keys as strings
          itemId: line.ItemId != null ? String(line.ItemId) : "",
          itemNumber: line.ItemNumber,
          count: line.Count,
          // the line stores a net; a line entered VAT-included is edited as its gross
          unitPrice: api.priceFieldValue(
            Number(line.UnitPrice),
            Boolean(line.PriceIncludesVat),
            vatRate,
          ),
          defaultPrice: null,
          priceIncludesVat: Boolean(line.PriceIncludesVat),
          description: line.Description,
        };
      });
    } catch (error) {
      api.showError(this, error);
      return [InvoiceEdit.emptyLine()];
    }
  }

  public onLineItemSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes its key into the draft itself; fill in what the row
    // shows alongside it
    const selected = event.getParameter("selectedItem");
    const rowContext = event.getSource().getBindingContext("invoiceDraft");
    if (!selected || !rowContext) {
      return;
    }
    const itemContext = selected.getBindingContext("odata");
    if (!itemContext) {
      return;
    }

    // the row's binding path ends in its index, e.g. "/lines/2"; a template
    // literal keeps the path checkable against InvoiceDraft
    const row = Number(rowContext.getPath().split("/").pop());
    const model = this.draft();
    model.setProperty(`/lines/${row}/itemNumber`, itemContext.getProperty("Number") as string);
    model.setProperty(
      `/lines/${row}/description`,
      itemContext.getProperty("Description") as string,
    );
    // the item's default price is pre-filled and stays overridable; an item
    // without one leaves the field empty so a price has to be typed
    const defaultPrice = itemContext.getProperty("DefaultPrice") as number | null;
    model.setProperty(`/lines/${row}/defaultPrice`, defaultPrice);
    model.setProperty(
      `/lines/${row}/unitPrice`,
      defaultPrice != null ? Number(defaultPrice) : null,
    );
  }

  public onAddLine(): void {
    const model = this.draft();
    const lines = model.getProperty("/lines");
    lines.push(InvoiceEdit.emptyLine());
    model.setProperty("/lines", lines);
  }

  private static emptyLine(): DraftLine {
    return {
      itemId: "",
      itemNumber: "",
      count: 1,
      unitPrice: null,
      defaultPrice: null,
      priceIncludesVat: false,
      description: "",
    };
  }

  public async onInvoiceDialogSave(): Promise<void> {
    // a second press while the first invoice is still being created would
    // create a second one; the busy dialog below blocks the interface, this
    // guard covers the moment before it is on screen
    if (this._savingInvoice) {
      return;
    }

    const draft = this.draft().getData();
    if (draft.lines.some((row) => !row.itemId)) {
      api.showError(this, new Error("ItemRequired"));
      return;
    }

    const lines = draft.lines.map((line) => ({
      itemId: Number(line.itemId),
      count: Number(line.count),
      // a price of 0 is a real value, not a missing one
      unitPrice: line.unitPrice == null || line.unitPrice === "" ? null : Number(line.unitPrice),
      priceIncludesVat: line.priceIncludesVat,
    }));

    // The ComboBox is bound two-way, so the draft holds the recipient the dialog
    // is showing even though the dialog itself is built once and reused.
    const recipientId = Number(draft.recipientId);

    // long-running task: creating an invoice renders its documents, so the
    // interface is blocked until it finishes
    this._savingInvoice = true;
    const busy = new BusyDialog({ title: this.i18nText("invoiceRunning") });
    busy.open();

    let failure: unknown = null;
    try {
      if (draft.invoiceId) {
        await api.createRevision(Number(draft.invoiceId), recipientId, lines);
      } else {
        await api.createInvoice(recipientId, lines);
      }

      this._invoiceDialog.close();
      await this.afterInvoiceSaved();
    } catch (error) {
      failure = error;
    } finally {
      // close the busy dialog before any error message opens
      busy.close();
      this._savingInvoice = false;
    }

    if (failure) {
      api.showError(this, failure);
    }
  }

  public onInvoiceDialogCancel(): void {
    this._invoiceDialog.close();
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
      // refresh the recipient ComboBox's list so the new row is included, then
      // select it in the parent invoice dialog, which stays open
      await this.refreshRecipients("recipient");
      this.draft().setProperty("/recipientId", String(id));
      this._quickDialog.close();
    } catch (error) {
      api.showError(this, error);
    }
  }

  public onQuickRecipientCancel(): void {
    this._quickDialog.close();
  }

  /** The draft lives on the dialog rather than the view, so it is read there. */
  private draft(): TypedJSONModel<InvoiceDraft> {
    return this._invoiceDialog.getModel("invoiceDraft") as TypedJSONModel<InvoiceDraft>;
  }

  private _savingInvoice = false;
  private _quickDialog!: Dialog;
  protected _invoiceDialog!: Dialog;
}
