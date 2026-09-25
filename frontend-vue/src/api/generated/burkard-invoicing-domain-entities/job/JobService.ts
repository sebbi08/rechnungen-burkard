import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import { EntityTypeServiceV4, ODataServiceOptionsInternal, EntitySetServiceV4 } from "@odata2ts/odata-service";
import type { Job, EditableJob, JobId } from "./Job.js";
import type { QJob } from "./QJob.js";
import { qJob, QJobId } from "./QJob.js";

export class JobService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<Job, EditableJob, QJob, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qJob, options);
    }
}

export class JobCollectionService<V extends ODataVersionV4 = "4.0"> extends EntitySetServiceV4<Job, EditableJob, QJob, JobId, JobService<V>, V> {
    constructor(client: ODataHttpClient, basePath: string, name: string, options?: ODataServiceOptionsInternal<V>) {
        super(client, basePath, name, qJob, new QJobId(name), options);
    }

    protected createEntityService(client: ODataHttpClient, path: string, name: string, options: ODataServiceOptionsInternal<V> | undefined) {
        return new JobService<V>(client, path, name, options);
    }
}
