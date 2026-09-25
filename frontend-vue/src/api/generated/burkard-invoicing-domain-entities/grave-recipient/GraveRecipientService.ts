import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import { EntityTypeServiceV4, ODataServiceOptionsInternal, EntitySetServiceV4 } from "@odata2ts/odata-service";
import type { GraveRecipient, EditableGraveRecipient, GraveRecipientId } from "./GraveRecipient.js";
import type { QGraveRecipient } from "./QGraveRecipient.js";
import { qGraveRecipient, QGraveRecipientId } from "./QGraveRecipient.js";
import { RecipientService } from "../recipient/RecipientService.js";

export class GraveRecipientService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<GraveRecipient, EditableGraveRecipient, QGraveRecipient, V> {
    private _Recipient?: RecipientService<V>;

    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qGraveRecipient, options);
    }

    public Recipient(): RecipientService<V> {
        if(!this._Recipient) {
          const { client, path, options } = this.__base;
          this._Recipient = new RecipientService(client, path, "Recipient", options)
        }

        return this._Recipient
    }
}

export class GraveRecipientCollectionService<V extends ODataVersionV4 = "4.0"> extends EntitySetServiceV4<GraveRecipient, EditableGraveRecipient, QGraveRecipient, GraveRecipientId, GraveRecipientService<V>, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qGraveRecipient, new QGraveRecipientId(name), options);
    }

    protected createEntityService(client: ODataHttpClient, path: string, name: string, options: ODataServiceOptionsInternal<V> | undefined) {
        return new GraveRecipientService<V>(client, path, name, options);
    }
}
