import type ResourceBundle from "sap/base/i18n/ResourceBundle";
import Device from "sap/ui/Device";
import UIComponent from "sap/ui/core/UIComponent";
import type View from "sap/ui/core/mvc/View";
import BindingMode from "sap/ui/model/BindingMode";
import JSONModel from "sap/ui/model/json/JSONModel";
import ResourceModel from "sap/ui/model/resource/ResourceModel";
import models from "./model/models";

/**
 * @namespace burkard.invoicing
 */
export default class Component extends UIComponent {
  public static metadata = {
    manifest: "json",
    interfaces: ["sap.ui.core.IAsyncContentCreation"],
  };

  public init(): void {
    super.init();
    this.setModel(models.createODataModel(), "odata");

    // Read-only view of sap.ui.Device: the dialogs bind their stretch property
    // to device>/system/phone so they open full screen on a phone only.
    const device = new JSONModel(Device);
    device.setDefaultBindingMode(BindingMode.OneWay);
    this.setModel(device, "device");

    this.getRouter().initialize();

    // the page title comes from the i18n bundle, not a hardcoded <title>
    const bundle = (
      this.getModel("i18n") as ResourceModel
    ).getResourceBundle() as ResourceBundle;
    document.title = bundle.getText("appTitle") ?? "";

    // The root view's onInit runs before the view is aggregated into the
    // component, so component models are not visible there yet. Bind the
    // OData root context once the root control has been loaded.
    void this.rootControlLoaded().then((vRootControl) => {
      if (vRootControl) {
        models.bindRoot(vRootControl as View);
      }
    });
  }
}
