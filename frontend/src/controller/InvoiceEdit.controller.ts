import { ComboBox$SelectionChangeEvent } from "sap/m/ComboBox";
import BusyDialog from "sap/m/BusyDialog";
import Input from "sap/m/Input";
import Select from "sap/m/Select";
import TextArea from "sap/m/TextArea";
import JSONModel from "sap/ui/model/json/JSONModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import * as api from "../model/api";
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
      // loadFragment passes this controller and the view ID prefix so that
      // this.byId(...) finds the dialog controls and IDs stay unique
      this._invoiceDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.InvoiceDialog",
      })) as any;
    }

    const lines =
      invoiceId != null ? await this.loadLines(invoiceId) : [InvoiceEdit.emptyLine()];
    this._invoiceDialog.setModel(
      new JSONModel({ recipientId, invoiceId, lines }),
      "invoiceDraft",
    );
    this._invoiceDialog.open();
  }

  private async loadLines(invoiceId: number): Promise<any[]> {
    try {
      const response = await fetch(`/odata/Invoices(${invoiceId})/Lines`);
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      const body = await response.json();
      return (body.value as any[]).map((line) => ({
        // the ComboBox matches its keys as strings
        itemId: line.ItemId != null ? String(line.ItemId) : "",
        itemNumber: line.ItemNumber,
        count: line.Count,
        unitPrice: line.UnitPrice,
        defaultPrice: null,
        description: line.Description,
      }));
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

    const path = rowContext.getPath();
    const model = rowContext.getModel() as JSONModel;
    model.setProperty(path + "/itemNumber", itemContext.getProperty("Number") as string);
    model.setProperty(
      path + "/description",
      itemContext.getProperty("Description") as string,
    );
    // the item's default price is pre-filled and stays overridable; an item
    // without one leaves the field empty so a price has to be typed
    const defaultPrice = itemContext.getProperty("DefaultPrice");
    model.setProperty(path + "/defaultPrice", defaultPrice);
    model.setProperty(
      path + "/unitPrice",
      defaultPrice != null ? Number(defaultPrice) : null,
    );
  }

  public onAddLine(): void {
    const model = this._invoiceDialog.getModel("invoiceDraft") as JSONModel;
    const lines = (model.getProperty("/lines") as any[]) ?? [];
    lines.push(InvoiceEdit.emptyLine());
    model.setProperty("/lines", lines);
  }

  private static emptyLine(): Record<string, unknown> {
    return {
      itemId: "",
      itemNumber: "",
      count: 1,
      unitPrice: null,
      defaultPrice: null,
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

    const model = this._invoiceDialog.getModel("invoiceDraft") as JSONModel;
    const draft = model.getData() as any;
    const rows = draft.lines as any[];
    if (rows.some((row) => !row.itemId)) {
      api.showError(this, new Error("ItemRequired"));
      return;
    }

    const lines = rows.map((line) => ({
      itemId: Number(line.itemId),
      count: Number(line.count),
      // a price of 0 is a real value, not a missing one
      unitPrice:
        line.unitPrice == null || line.unitPrice === "" ? null : Number(line.unitPrice),
    }));

    // The invoice goes to the recipient the dialog shows. The dialog is built
    // once and reused, so its select keeps the selection of the previous open
    // while the draft of this open starts out empty — reading the draft would
    // send recipient 0 and fail with RecipientNotFound.
    const recipientId = Number((this.byId("recipient") as Select).getSelectedKey());

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
      })) as any;
    }
    (this.byId("quickName") as Input).setValue("");
    (this.byId("quickAddress") as TextArea).setValue("");
    this._quickDialog.open();
  }

  public async onQuickRecipientSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    try {
      const id = await api.createRecipient({
        Name: (this.byId("quickName") as Input).getValue(),
        Address: (this.byId("quickAddress") as TextArea).getValue(),
      });
      // refresh the recipient select's list so the new row is included,
      // then select it in the parent invoice dialog, which stays open
      const select = this.byId("recipient") as Select;
      await (select.getBinding("items") as ODataListBinding).refresh();
      (this._invoiceDialog.getModel("invoiceDraft") as JSONModel).setProperty(
        "/recipientId",
        id,
      );
      dialog.close();
    } catch (error) {
      api.showError(this, error);
    }
  }

  public onQuickRecipientCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  private _savingInvoice = false;
  private _quickDialog: any;
  protected _invoiceDialog: any;
}
