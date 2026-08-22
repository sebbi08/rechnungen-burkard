import BaseController from "./Base.controller";
import * as api from "../model/api";
import Context from "sap/ui/model/odata/v4/Context";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Input from "sap/m/Input";
import TextArea from "sap/m/TextArea";
import MessageBox from "sap/m/MessageBox";

export default class Settings extends BaseController {
    public onInit(): void {
        const view = this.getView()!;
        // routed views run onInit before they are added to the App, so the
        // odata model is not visible via view.getModel() yet
        const model = this.getOwnerComponent()!.getModel("odata") as ODataModel;
        // bind the view to the /Settings singleton; tables use absolute paths
        const context = model.createBindingContext("/Settings") as Context;
        view.setBindingContext(context, "odata");
    }

    public async onSave(): Promise<void> {
        try {
            await api.patchSettings({
                StampWord: (this.byId("stampWord") as Input).getValue(),
                EndingText: (this.byId("endingText") as TextArea).getValue(),
            });
            await MessageBox.information((this.getView()!.getModel("i18n") as any)
                .getResourceBundle().getText("saved"));
        } catch (error) {
            await api.showError(this, error);
        }
    }

    public async onAddVatRate(): Promise<void> {
        const input = this.byId("newVatRate") as Input;
        const rate = Number(input.getValue());
        if (!rate || rate <= 0) {
            await api.showError(this, new Error("InvalidVatRate"));
            return;
        }

        try {
            await api.createVatRate(rate);
            input.setValue("");
            this.refreshVatRates();
        } catch (error) {
            await api.showError(this, error);
        }
    }

    public async onDeleteVatRate(event: any): Promise<void> {
        const context = (event.getSource().getBindingContext("odata") as Context);
        const id = context.getProperty("Id") as number;
        try {
            await api.deleteVatRate(id);
            this.refreshVatRates();
        } catch (error) {
            await api.showError(this, error);
        }
    }

    private refreshVatRates(): void {
        const table = this.byId("vatRates");
        const binding = (table as any).getBinding("items") as ODataListBinding;
        binding.refresh();
    }
}
