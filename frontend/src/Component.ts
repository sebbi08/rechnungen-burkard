import type ResourceBundle from "sap/base/i18n/ResourceBundle";
import UIComponent from "sap/ui/core/UIComponent";
import type View from "sap/ui/core/mvc/View";
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
    this.getRouter().initialize();

    // the page title comes from the i18n bundle, not a hardcoded <title>
    const bundle = (
      this.getModel("i18n") as ResourceModel
    ).getResourceBundle() as ResourceBundle;
    document.title = bundle.getText("appTitle") ?? "";

    // The root view's onInit runs before the view is aggregated into the
    // component, so component models are not visible there yet. Bind the
    // OData root context once the root control has been loaded.
    this.rootControlLoaded().then((vRootControl) => {
      if (vRootControl) {
        models.bindRoot(vRootControl as View);
      }
    });
  }
}
