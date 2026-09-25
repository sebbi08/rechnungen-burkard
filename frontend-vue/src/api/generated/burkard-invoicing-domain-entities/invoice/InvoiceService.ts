import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { Invoice, EditableInvoice, InvoiceId } from "./Invoice.js";
import type { QInvoice } from "./QInvoice.js";
import type { InvoiceLineId } from "../invoice-line/InvoiceLine.js";
import type { DocumentId } from "../document/Document.js";
import { qInvoice, QInvoiceId } from "./QInvoice.js";
import {
  InvoiceLineService,
  InvoiceLineCollectionService,
} from "../invoice-line/InvoiceLineService.js";
import { DocumentService, DocumentCollectionService } from "../document/DocumentService.js";

export class InvoiceService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  Invoice,
  EditableInvoice,
  QInvoice,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qInvoice, options);
  }

  public Lines(): InvoiceLineCollectionService<V>;
  public Lines(id: InvoiceLineId): InvoiceLineService<V>;
  public Lines(id?: InvoiceLineId | undefined) {
    const fieldName = "Lines";
    const { client, path, options } = this.__base;
    const collection = new InvoiceLineCollectionService(client, path, fieldName, {
      ...options,
      subtype: false,
    });
    return typeof id === "undefined" || id === null ? collection : collection.byId(id);
  }

  public Documents(): DocumentCollectionService<V>;
  public Documents(id: DocumentId): DocumentService<V>;
  public Documents(id?: DocumentId | undefined) {
    const fieldName = "Documents";
    const { client, path, options } = this.__base;
    const collection = new DocumentCollectionService(client, path, fieldName, {
      ...options,
      subtype: false,
    });
    return typeof id === "undefined" || id === null ? collection : collection.byId(id);
  }
}

export class InvoiceCollectionService<V extends ODataVersionV4 = "4.0"> extends EntitySetServiceV4<
  Invoice,
  EditableInvoice,
  QInvoice,
  InvoiceId,
  InvoiceService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qInvoice, new QInvoiceId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new InvoiceService<V>(client, path, name, options);
  }
}
