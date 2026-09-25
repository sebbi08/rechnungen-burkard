import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import { EntityTypeServiceV4, ODataServiceOptionsInternal, EntitySetServiceV4 } from "@odata2ts/odata-service";
import type { Document, EditableDocument, DocumentId } from "./Document.js";
import type { QDocument } from "./QDocument.js";
import { qDocument, QDocumentId } from "./QDocument.js";
import { InvoiceService } from "../invoice/InvoiceService.js";

export class DocumentService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<Document, EditableDocument, QDocument, V> {
    private _Invoice?: InvoiceService<V>;

    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qDocument, options);
    }

    public Invoice(): InvoiceService<V> {
        if(!this._Invoice) {
          const { client, path, options } = this.__base;
          this._Invoice = new InvoiceService(client, path, "Invoice", options)
        }

        return this._Invoice
    }
}

export class DocumentCollectionService<V extends ODataVersionV4 = "4.0"> extends EntitySetServiceV4<Document, EditableDocument, QDocument, DocumentId, DocumentService<V>, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qDocument, new QDocumentId(name), options);
    }

    protected createEntityService(client: ODataHttpClient, path: string, name: string, options: ODataServiceOptionsInternal<V> | undefined) {
        return new DocumentService<V>(client, path, name, options);
    }
}
