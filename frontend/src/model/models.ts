import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import type Context from "sap/ui/model/odata/v4/Context";
import type View from "sap/ui/core/mvc/View";

export default {
    createODataModel(): ODataModel {
        return new ODataModel({
            serviceUrl: "/odata/",
            synchronizationMode: "None",
            // list-binding filters (search fields, half-year filter) require
            // Server mode; the app never queues client-side changes, so all
            // writes stay on the server anyway
            operationMode: "Server",
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
