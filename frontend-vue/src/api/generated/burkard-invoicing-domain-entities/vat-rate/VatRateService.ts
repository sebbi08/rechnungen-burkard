import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import { EntityTypeServiceV4, ODataServiceOptionsInternal, EntitySetServiceV4 } from "@odata2ts/odata-service";
import type { VatRate, EditableVatRate, VatRateId } from "./VatRate.js";
import type { QVatRate } from "./QVatRate.js";
import { qVatRate, QVatRateId } from "./QVatRate.js";

export class VatRateService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<VatRate, EditableVatRate, QVatRate, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qVatRate, options);
    }
}

export class VatRateCollectionService<V extends ODataVersionV4 = "4.0"> extends EntitySetServiceV4<VatRate, EditableVatRate, QVatRate, VatRateId, VatRateService<V>, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qVatRate, new QVatRateId(name), options);
    }

    protected createEntityService(client: ODataHttpClient, path: string, name: string, options: ODataServiceOptionsInternal<V> | undefined) {
        return new VatRateService<V>(client, path, name, options);
    }
}
