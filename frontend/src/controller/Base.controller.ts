import Router from "sap/m/routing/Router";
import type { NavigationList$ItemSelectEvent } from "sap/tnt/NavigationList";
import Controller from "sap/ui/core/mvc/Controller";
import UIComponent from "sap/ui/core/UIComponent";

export default abstract class BaseController extends Controller {
  public onNavSelect(event: NavigationList$ItemSelectEvent): void {
    const key = event.getParameter("item")?.getKey();
    if (key) {
      (UIComponent.getRouterFor(this) as Router).navTo(key);
    }
  }

  protected i18nText(key: string): string {
    return (this.getView()!.getModel("i18n") as any)
      .getResourceBundle()
      .getText(key);
  }
}
