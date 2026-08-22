import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import type Context from "sap/ui/model/odata/v4/Context";
import type View from "sap/ui/core/mvc/View";

export default {
    createODataModel(): ODataModel {
        return new ODataModel({
            serviceUrl: "/odata/",
            synchronizationMode: "None",
            // the backend does not expose a $batch endpoint, so send requests directly
            groupId: "$direct",
        });
    },

    bindRoot(view: View): void {
        const model = view.getModel("odata") as ODataModel;
        const context = model.createBindingContext("/") as Context;
        view.setBindingContext(context, "odata");
    },
};
