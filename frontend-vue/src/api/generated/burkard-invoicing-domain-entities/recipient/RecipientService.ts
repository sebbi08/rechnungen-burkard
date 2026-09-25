import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { Recipient, EditableRecipient, RecipientId } from "./Recipient.js";
import type { QRecipient } from "./QRecipient.js";
import { qRecipient, QRecipientId } from "./QRecipient.js";

export class RecipientService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  Recipient,
  EditableRecipient,
  QRecipient,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qRecipient, options);
  }
}

export class RecipientCollectionService<
  V extends ODataVersionV4 = "4.0",
> extends EntitySetServiceV4<
  Recipient,
  EditableRecipient,
  QRecipient,
  RecipientId,
  RecipientService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qRecipient, new QRecipientId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new RecipientService<V>(client, path, name, options);
  }
}
