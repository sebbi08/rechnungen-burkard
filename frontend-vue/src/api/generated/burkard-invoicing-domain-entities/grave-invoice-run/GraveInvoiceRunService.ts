import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import { EntityTypeServiceV4, ODataServiceOptionsInternal, EntitySetServiceV4 } from "@odata2ts/odata-service";
import type { GraveInvoiceRun, EditableGraveInvoiceRun, GraveInvoiceRunId } from "./GraveInvoiceRun.js";
import type { QGraveInvoiceRun } from "./QGraveInvoiceRun.js";
import { qGraveInvoiceRun, QGraveInvoiceRunId } from "./QGraveInvoiceRun.js";

export class GraveInvoiceRunService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<GraveInvoiceRun, EditableGraveInvoiceRun, QGraveInvoiceRun, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qGraveInvoiceRun, options);
    }
}

export class GraveInvoiceRunCollectionService<V extends ODataVersionV4 = "4.0"> extends EntitySetServiceV4<GraveInvoiceRun, EditableGraveInvoiceRun, QGraveInvoiceRun, GraveInvoiceRunId, GraveInvoiceRunService<V>, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qGraveInvoiceRun, new QGraveInvoiceRunId(name), options);
    }

    protected createEntityService(client: ODataHttpClient, path: string, name: string, options: ODataServiceOptionsInternal<V> | undefined) {
        return new GraveInvoiceRunService<V>(client, path, name, options);
    }
}
