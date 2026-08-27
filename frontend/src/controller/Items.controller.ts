import Input from "sap/m/Input";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import Context from "sap/ui/model/odata/v4/Context";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import * as api from "../model/api";
import BaseController from "./Base.controller";

export default class Items extends BaseController {
  public onInit(): void {
    // routed views run onInit before they are added to the App, so the
    // odata model is not visible via view.getModel() yet
    const view = this.getView()!;
    const model = this.getOwnerComponent()!.getModel("odata") as ODataModel;
    view.setBindingContext(model.createBindingContext("/") as Context, "odata");
  }

  public onSearch(event: any): void {
    const term = event.getParameter("query") as string;
    const binding = (this.byId("itemsTable") as any).getBinding(
      "items",
    ) as ODataListBinding;
    if (!term) {
      binding.filter([]);
      return;
    }

    binding.filter(
      new Filter({
        filters: [
          new Filter("Number", FilterOperator.Contains, term),
          new Filter("Description", FilterOperator.Contains, term),
        ],
        and: false,
      }),
    );
  }

  public async onCreateItem(): Promise<void> {
    await this.openItemDialog(null);
  }

  public async onEditItem(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("odata") as Context;
    await this.openItemDialog(context);
  }

  private async openItemDialog(context: Context | null): Promise<void> {
    if (!this._dialog) {
      // loadFragment passes this controller and the view ID prefix so that
      // this.byId(...) finds the dialog controls and IDs stay unique; the
      // fragment must be reused on later opens or the view-prefixed child
      // IDs collide with the first instance
      this._dialog = (await this.loadFragment({
        name: "burkard.invoicing.view.fragments.ItemDialog",
      })) as any;
    }

    if (context) {
      (this.byId("itemNumber") as Input).setValue(
        context.getProperty("Number") as string,
      );
      (this.byId("itemDescription") as Input).setValue(
        context.getProperty("Description") as string,
      );
      (this.byId("itemVatRate") as Input).setValue(
        String(context.getProperty("VatRate")),
      );
      (this.byId("itemDefaultPrice") as Input).setValue(
        context.getProperty("DefaultPrice") != null
          ? String(context.getProperty("DefaultPrice"))
          : "",
      );
      this._editId = context.getProperty("Id") as number;
    } else {
      (this.byId("itemNumber") as Input).setValue("");
      (this.byId("itemDescription") as Input).setValue("");
      (this.byId("itemVatRate") as Input).setValue("");
      (this.byId("itemDefaultPrice") as Input).setValue("");
      this._editId = null;
    }
    this._dialog.setTitle(this.i18nText(context ? "editItem" : "createItem"));
    this._dialog.open();
  }

  public async onDialogSave(event: any): Promise<void> {
    const dialog = event.getSource().getParent() as any;
    const rawPrice = (this.byId("itemDefaultPrice") as Input).getValue();
    const fields = {
      Number: (this.byId("itemNumber") as Input).getValue(),
      Description: (this.byId("itemDescription") as Input).getValue(),
      VatRate: Number((this.byId("itemVatRate") as Input).getValue()),
      // an empty input means "no default price"; a price of 0 is a real value
      DefaultPrice: rawPrice === "" ? null : Number(rawPrice),
    };
    try {
      if (this._editId != null) {
        await api.updateItem(this._editId, fields);
      } else {
        await api.createItem(fields);
      }
      dialog.close();
      this._editId = null;
      (
        (this.byId("itemsTable") as any).getBinding("items") as ODataListBinding
      ).refresh();
    } catch (error) {
      await api.showError(this, error);
    }
  }

  public onDialogCancel(event: any): void {
    (event.getSource().getParent() as any).close();
  }

  private _editId: number | null = null;
  private _dialog: any;
}
