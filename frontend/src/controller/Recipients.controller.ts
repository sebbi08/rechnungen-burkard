import type { Button$PressEvent } from "sap/m/Button";
import Dialog from "sap/m/Dialog";
import type { SearchField$SearchEvent } from "sap/m/SearchField";
import TypedJSONModel from "sap/ui/model/json/TypedJSONModel";
import Context from "sap/ui/model/odata/v4/Context";
import * as api from "../model/api";
import type { RecipientDraft, RecipientsView } from "../model/state";
import BaseController from "./Base.controller";

export default class Recipients extends BaseController {
  public onInit(): void {
    this.bindODataRoot();
    this.setState<RecipientsView>("view", { searchTerm: null });
  }

  public onSearch(event: SearchField$SearchEvent): void {
    // the table's filter is bound to this term; null makes it neutral, which
    // drops it, so an empty search needs no special case
    const term = event.getParameter("query") ?? "";
    this.state<RecipientsView>("view").setProperty("/searchTerm", term || null);
  }

  public async onCreateRecipient(): Promise<void> {
    await this.openRecipientDialog(null);
  }

  public async onEditRecipient(event: Button$PressEvent): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openRecipientDialog(context);
  }

  private async openRecipientDialog(context: Context | null): Promise<void> {
    if (!this._dialog) {
      // loadFragment passes the view ID prefix, so the fragment has to be
      // reused on later opens or its prefixed child IDs collide with the
      // first instance
      this._dialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.RecipientDialog",
      })) as Dialog;
    }

    this._dialog.setModel(
      new TypedJSONModel<RecipientDraft>(
        context
          ? {
              id: context.getProperty("Id") as number,
              number: context.getProperty("Number") as string,
              name: context.getProperty("Name") as string,
              address: context.getProperty("Address") as string,
            }
          : { id: null, number: "", name: "", address: "" },
      ),
      "recipientDraft",
    );
    this._dialog.open();
  }

  public async onDialogSave(): Promise<void> {
    const draft = this.draft().getData();
    const number = draft.number.trim();
    const fields = { Name: draft.name, Address: draft.address };
    try {
      if (draft.id != null) {
        await api.updateRecipient(draft.id, { ...fields, Number: number });
      } else {
        // an empty number means "assign the next free one"
        await api.createRecipient(number ? { ...fields, Number: number } : fields);
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
  private draft(): TypedJSONModel<RecipientDraft> {
    return this._dialog.getModel("recipientDraft") as TypedJSONModel<RecipientDraft>;
  }

  private _dialog!: Dialog;
}
