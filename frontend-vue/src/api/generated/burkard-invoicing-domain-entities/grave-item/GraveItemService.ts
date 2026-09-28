import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { GraveItem, EditableGraveItem, GraveItemId } from "./GraveItem.js";
import type { QGraveItem } from "./QGraveItem.js";
import { qGraveItem, QGraveItemId } from "./QGraveItem.js";
import { InvoiceItemService } from "../invoice-item/InvoiceItemService.js";

export class GraveItemService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  GraveItem,
  EditableGraveItem,
  QGraveItem,
  V
> {
  private _Item?: InvoiceItemService<V>;

  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qGraveItem, options);
  }

  public Item(): InvoiceItemService<V> {
    if (!this._Item) {
      const { client, path, options } = this.__base;
      this._Item = new InvoiceItemService(client, path, "Item", options);
    }

    return this._Item;
  }
}

export class GraveItemCollectionService<
  V extends ODataVersionV4 = "4.0",
> extends EntitySetServiceV4<
  GraveItem,
  EditableGraveItem,
  QGraveItem,
  GraveItemId,
  GraveItemService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qGraveItem, new QGraveItemId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new GraveItemService<V>(client, path, name, options);
  }
}
