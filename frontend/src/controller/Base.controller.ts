import Router from "sap/m/routing/Router";
import Controller from "sap/ui/core/mvc/Controller";
import UIComponent from "sap/ui/core/UIComponent";

export default abstract class BaseController extends Controller {
  public onNavBack(): void {
    (UIComponent.getRouterFor(this) as Router).navTo("home");
  }

  protected i18nText(key: string): string {
    return (this.getView()!.getModel("i18n") as any)
      .getResourceBundle()
      .getText(key);
  }
}
