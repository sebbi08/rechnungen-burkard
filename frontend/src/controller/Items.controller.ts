import type { Button$PressEvent } from "sap/m/Button";
import Dialog from "sap/m/Dialog";
import type { SearchField$SearchEvent } from "sap/m/SearchField";
import TypedJSONModel from "sap/ui/model/json/TypedJSONModel";
import Context from "sap/ui/model/odata/v4/Context";
import * as api from "../model/api";
import type { ItemDraft, ItemsView } from "../model/state";
import BaseController from "./Base.controller";

export default class Items extends BaseController {
  public onInit(): void {
    this.bindODataRoot();
    this.setState<ItemsView>("view", { searchTerm: null });
  }

  public onSearch(event: SearchField$SearchEvent): void {
    // The table's filters are bound to this term. A null value makes a filter
    // neutral and a neutral filter is dropped, so clearing the field needs no
    // special case here.
    const term = event.getParameter("query") ?? "";
    this.state<ItemsView>("view").setProperty("/searchTerm", term || null);
  }

  public async onCreateItem(): Promise<void> {
    await this.openItemDialog(null);
  }

  public async onEditItem(event: Button$PressEvent): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openItemDialog(context);
  }

  private async openItemDialog(context: Context | null): Promise<void> {
    if (!this._dialog) {
      // loadFragment passes the view ID prefix, so the fragment has to be
      // reused on later opens or its prefixed child IDs collide with the
      // first instance
      this._dialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.ItemDialog",
      })) as Dialog;
    }

    // The draft is read from the context before the refresh below: a refresh
    // drops the model's caches, and a context whose cache is gone answers
    // getProperty with undefined.
    const defaultPrice = context?.getProperty("DefaultPrice") as number | null;
    this._dialog.setModel(
      new TypedJSONModel<ItemDraft>(
        context
          ? {
              id: context.getProperty("Id") as number,
              number: context.getProperty("Number") as string,
              description: context.getProperty("Description") as string,
              vatRate: String(context.getProperty("VatRate") as number),
              defaultPrice: defaultPrice != null ? String(defaultPrice) : "",
            }
          : {
              id: null,
              number: "",
              description: "",
              vatRate: "",
              defaultPrice: "",
            },
      ),
      "itemDraft",
    );

    // the fragment is reused, so force the VAT list to re-read from the
    // service; otherwise rates added or deleted in the settings stay stale
    this.odata().refresh();
    this._dialog.open();
  }

  public async onDialogSave(): Promise<void> {
    const draft = this.draft().getData();
    const fields = {
      Number: draft.number,
      Description: draft.description,
      VatRate: Number(draft.vatRate),
      // an empty input means "no default price"; a price of 0 is a real value
      DefaultPrice: draft.defaultPrice === "" ? null : Number(draft.defaultPrice),
    };
    try {
      if (draft.id != null) {
        await api.updateItem(draft.id, fields);
      } else {
        await api.createItem(fields);
      }
      this._dialog.close();
      this.odata().refresh();
    } catch (error) {
      api.showError(this, error);
    }
  }

  public onDialogCancel(): void {
    this._dialog.close();
  }

  /** The draft lives on the dialog rather than the view, so it is read there. */
  private draft(): TypedJSONModel<ItemDraft> {
    return this._dialog.getModel("itemDraft") as TypedJSONModel<ItemDraft>;
  }

  private _dialog!: Dialog;
}
