import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { InvoiceLine, EditableInvoiceLine, InvoiceLineId } from "./InvoiceLine.js";
import type { QInvoiceLine } from "./QInvoiceLine.js";
import { qInvoiceLine, QInvoiceLineId } from "./QInvoiceLine.js";

export class InvoiceLineService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  InvoiceLine,
  EditableInvoiceLine,
  QInvoiceLine,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qInvoiceLine, options);
  }
}

export class InvoiceLineCollectionService<
  V extends ODataVersionV4 = "4.0",
> extends EntitySetServiceV4<
  InvoiceLine,
  EditableInvoiceLine,
  QInvoiceLine,
  InvoiceLineId,
  InvoiceLineService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qInvoiceLine, new QInvoiceLineId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new InvoiceLineService<V>(client, path, name, options);
  }
}
