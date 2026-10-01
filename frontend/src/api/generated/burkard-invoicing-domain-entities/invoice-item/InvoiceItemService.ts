import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { InvoiceItem, EditableInvoiceItem, InvoiceItemId } from "./InvoiceItem.js";
import type { QInvoiceItem } from "./QInvoiceItem.js";
import { qInvoiceItem, QInvoiceItemId } from "./QInvoiceItem.js";

export class InvoiceItemService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  InvoiceItem,
  EditableInvoiceItem,
  QInvoiceItem,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qInvoiceItem, options);
  }
}

export class InvoiceItemCollectionService<
  V extends ODataVersionV4 = "4.0",
> extends EntitySetServiceV4<
  InvoiceItem,
  EditableInvoiceItem,
  QInvoiceItem,
  InvoiceItemId,
  InvoiceItemService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qInvoiceItem, new QInvoiceItemId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new InvoiceItemService<V>(client, path, name, options);
  }
}
