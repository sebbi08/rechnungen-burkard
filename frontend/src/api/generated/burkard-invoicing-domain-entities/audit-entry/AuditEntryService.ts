import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { AuditEntry, EditableAuditEntry, AuditEntryId } from "./AuditEntry.js";
import type { QAuditEntry } from "./QAuditEntry.js";
import { qAuditEntry, QAuditEntryId } from "./QAuditEntry.js";

export class AuditEntryService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  AuditEntry,
  EditableAuditEntry,
  QAuditEntry,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qAuditEntry, options);
  }
}

export class AuditEntryCollectionService<
  V extends ODataVersionV4 = "4.0",
> extends EntitySetServiceV4<
  AuditEntry,
  EditableAuditEntry,
  QAuditEntry,
  AuditEntryId,
  AuditEntryService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qAuditEntry, new QAuditEntryId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new AuditEntryService<V>(client, path, name, options);
  }
}
