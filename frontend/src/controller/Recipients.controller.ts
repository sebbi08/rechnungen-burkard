import BaseController from "./Base.controller";
import * as api from "../model/api";
import Context from "sap/ui/model/odata/v4/Context";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import Input from "sap/m/Input";

export default class Recipients extends BaseController {
    public onInit(): void {
        // routed views run onInit before they are added to the App, so the
        // odata model is not visible via view.getModel() yet
        const view = this.getView()!;
        const model = this.getOwnerComponent()!.getModel("odata") as ODataModel;
        view.setBindingContext(model.createBindingContext("/") as Context, "odata");
    }

    public onSearch(event: any): void {
        const term = event.getParameter("query") as string;
        const binding = (this.byId("recipientsTable") as any).getBinding("items") as ODataListBinding;
        if (!term) {
            binding.filter([]);
            return;
        }

        binding.filter(new Filter("Name", FilterOperator.Contains, term));
    }

    public async onCreateRecipient(): Promise<void> {
        if (!this._dialog) {
            // loadFragment passes this controller and the view ID prefix so that
            // this.byId(...) finds the dialog controls and IDs stay unique; the
            // fragment must be reused on later opens or the view-prefixed child
            // IDs collide with the first instance
            this._dialog = await this.loadFragment({
                name: "burkard.invoicing.view.fragments.RecipientDialog",
            }) as any;
        }
        this._dialog.open();
    }

    public async onDialogSave(event: any): Promise<void> {
        const dialog = event.getSource().getParent() as any;
        try {
            await api.createRecipient({
                Name: (this.byId("recipientName") as Input).getValue(),
                Address: (this.byId("recipientAddress") as Input).getValue(),
            });
            dialog.close();
            ((this.byId("recipientsTable") as any).getBinding("items") as ODataListBinding).refresh();
        } catch (error) {
            await api.showError(this, error);
        }
    }

    public onDialogCancel(event: any): void {
        (event.getSource().getParent() as any).close();
    }

    private _dialog: any;
}
