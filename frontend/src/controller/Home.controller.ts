import type { GenericTile$PressEvent } from "sap/m/GenericTile";
import Router from "sap/m/routing/Router";
import UIComponent from "sap/ui/core/UIComponent";
import BaseController from "./Base.controller";

export default class Home extends BaseController {
  public onTilePress(event: GenericTile$PressEvent): void {
    const route = event
      .getSource()
      .getCustomData()
      .find((data) => data.getKey() === "route");
    if (route) {
      (UIComponent.getRouterFor(this) as Router).navTo(route.getValue() as string);
    }
  }
}
