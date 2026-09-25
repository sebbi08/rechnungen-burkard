import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import { EntityTypeServiceV4, ODataServiceOptionsInternal, EntitySetServiceV4 } from "@odata2ts/odata-service";
import type { Grave, EditableGrave, GraveId } from "./Grave.js";
import type { QGrave } from "./QGrave.js";
import type { GraveItemId } from "../grave-item/GraveItem.js";
import type { GraveRecipientId } from "../grave-recipient/GraveRecipient.js";
import { qGrave, QGraveId } from "./QGrave.js";
import { GraveItemService, GraveItemCollectionService } from "../grave-item/GraveItemService.js";
import { GraveRecipientService, GraveRecipientCollectionService } from "../grave-recipient/GraveRecipientService.js";

export class GraveService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<Grave, EditableGrave, QGrave, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qGrave, options);
    }

    public Items(): GraveItemCollectionService<V>;
    public Items(id: GraveItemId): GraveItemService<V>;
    public Items(id?: GraveItemId | undefined) {
        const fieldName = "Items";
        const { client, path, options } = this.__base;
        const collection = new GraveItemCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public Recipients(): GraveRecipientCollectionService<V>;
    public Recipients(id: GraveRecipientId): GraveRecipientService<V>;
    public Recipients(id?: GraveRecipientId | undefined) {
        const fieldName = "Recipients";
        const { client, path, options } = this.__base;
        const collection = new GraveRecipientCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }
}

export class GraveCollectionService<V extends ODataVersionV4 = "4.0"> extends EntitySetServiceV4<Grave, EditableGrave, QGrave, GraveId, GraveService<V>, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qGrave, new QGraveId(name), options);
    }

    protected createEntityService(client: ODataHttpClient, path: string, name: string, options: ODataServiceOptionsInternal<V> | undefined) {
        return new GraveService<V>(client, path, name, options);
    }
}
