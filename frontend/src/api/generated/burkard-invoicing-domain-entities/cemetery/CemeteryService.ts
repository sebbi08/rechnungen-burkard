import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { Cemetery, EditableCemetery, CemeteryId } from "./Cemetery.js";
import type { QCemetery } from "./QCemetery.js";
import { qCemetery, QCemeteryId } from "./QCemetery.js";

export class CemeteryService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  Cemetery,
  EditableCemetery,
  QCemetery,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qCemetery, options);
  }
}

export class CemeteryCollectionService<V extends ODataVersionV4 = "4.0"> extends EntitySetServiceV4<
  Cemetery,
  EditableCemetery,
  QCemetery,
  CemeteryId,
  CemeteryService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qCemetery, new QCemeteryId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new CemeteryService<V>(client, path, name, options);
  }
}
