import { ComboBox$SelectionChangeEvent } from "sap/m/ComboBox";
import Input from "sap/m/Input";
import Select from "sap/m/Select";
import TextArea from "sap/m/TextArea";
import JSONModel from "sap/ui/model/json/JSONModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import * as api from "../model/api";
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
      // loadFragment passes this controller and the view ID prefix so that
      // this.byId(...) finds the dialog controls and IDs stay unique
      this._graveDialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.GraveDialog",
      })) as any;
    }

    // everything the dialog collects lives in this draft until it is saved, so
    // items and recipients can be added before the grave itself exists
    this._graveDialog.setModel(
      new JSONModel(
        graveId != null
          ? await this.loadGraveDraft(graveId)
          : {
              Id: null,
              GraveNumber: "",
              StartDate: "",
              EndDate: "",
              Items: [],
              Recipients: [],
            },
      ),
      "graveDraft",
    );

    this._graveDialog.open();
  }

  private async loadGraveDraft(graveId: number): Promise<any> {
    const details = await api.fetchGraveDetails(graveId);
    return {
      Id: details.Id,
      GraveNumber: details.GraveNumber,
      StartDate: details.StartDate,
      EndDate: details.EndDate,
      Items: details.Items.map((link: any) => ({
        // the ComboBox matches its keys as strings
        itemId: String(link.Item.Id),
        itemNumber: link.Item.Number,
        description: link.Item.Description,
        count: link.Count,
        unitPrice: link.UnitPrice,
        defaultPrice: link.Item.DefaultPrice,
      })),
      Recipients: details.Recipients.map((link: any) => ({
        recipientId: String(link.Recipient.Id),
        name: link.Recipient.Name,
      })),
    };
  }

  public onGraveItemSelected(event: ComboBox$SelectionChangeEvent): void {
    // the ComboBox writes the key itself; fill in what the row shows alongside it
    const itemContext = event.getParameter("selectedItem")?.getBindingContext("odata");
    const rowContext = event.getSource().getBindingContext("graveDraft");
    if (!itemContext || !rowContext) {
      return;
    }

    const model = rowContext.getModel() as JSONModel;
    const path = rowContext.getPath();
    model.setProperty(path + "/itemNumber", itemContext.getProperty("Number") as string);
    model.setProperty(path + "/description", itemContext.getProperty("Description") as string);
    // an empty price falls back to this, which the input shows as its placeholder
    model.setProperty(path + "/defaultPrice", itemContext.getProperty("DefaultPrice"));
  }

  public onAddItemLink(): void {
    const model = this._graveDialog.getModel("graveDraft") as JSONModel;
    const rows = (model.getProperty("/Items") as any[]) ?? [];
    rows.push({
      itemId: "",
      itemNumber: "",
      description: "",
      count: 1,
      unitPrice: null,
      defaultPrice: null,
    });
    model.setProperty("/Items", rows);
  }

  public async onAddRecipientLink(): Promise<void> {
    const select = this.byId("linkRecipient") as Select;
    const selected = select.getSelectedItem();
    if (!selected) {
      return;
    }

    const model = this._graveDialog.getModel("graveDraft") as JSONModel;
    const rows = (model.getProperty("/Recipients") as any[]) ?? [];
    const recipientId = select.getSelectedKey();
    if (rows.some((row) => row.recipientId === recipientId)) {
      await api.showError(this, new Error("GraveRecipientLinkExists"));
      return;
    }

    rows.push({ recipientId, name: selected.getText() });
    model.setProperty("/Recipients", rows);
  }

  public onDeleteItemLink(event: any): void {
    this.removeDraftRow(event, "Items");
  }

  public onDeleteRecipientLink(event: any): void {
    this.removeDraftRow(event, "Recipients");
  }

  private removeDraftRow(event: any, collection: string): void {
    const context = event.getSource().getBindingContext("graveDraft") as any;
    const model = context.getModel() as JSONModel;
    const rows = model.getProperty(`/${collection}`) as any[];
    // the row's binding path ends in its index, e.g. "/Items/2"
    rows.splice(Number(context.getPath().split("/").pop()), 1);
    model.setProperty(`/${collection}`, rows);
  }

  public async onGraveDialogSave(): Promise<void> {
    const model = this._graveDialog.getModel("graveDraft") as JSONModel;
    const draft = model.getData() as any;
    const items = draft.Items as any[];
    if (items.some((row) => !row.itemId)) {
      await api.showError(this, new Error("ItemRequired"));
      return;
    }

    try {
      await api.saveGrave({
        graveId: draft.Id != null ? Number(draft.Id) : null,
        graveNumber: draft.GraveNumber ?? "",
        startDate: draft.StartDate ?? "",
        endDate: draft.EndDate ?? "",
        items: items.map((row) => ({
          itemId: Number(row.itemId),
          count: Number(row.count),
          // a price of 0 is a real value, not a missing one
          unitPrice:
            row.unitPrice == null || row.unitPrice === "" ? null : Number(row.unitPrice),
        })),
        recipientIds: (draft.Recipients as any[]).map((row) => Number(row.recipientId)),
      });

      this._graveDialog.close();
      await this.refreshAfterSave();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onGraveDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
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
      // refresh the link select's list so the new row is included, then
      // select it; the parent grave dialog stays open
      const select = this.byId("linkRecipient") as Select;
      await (select.getBinding("items") as ODataListBinding).refresh();
      select.setSelectedKey(String(id));
      dialog.close();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onQuickRecipientCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  protected _graveDialog: any;
  private _quickDialog: any;
}
