import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { GlobalSettings, EditableGlobalSettings, GlobalSettingsId } from "./GlobalSettings.js";
import type { QGlobalSettings } from "./QGlobalSettings.js";
import { qGlobalSettings, QGlobalSettingsId } from "./QGlobalSettings.js";

export class GlobalSettingsService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  GlobalSettings,
  EditableGlobalSettings,
  QGlobalSettings,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qGlobalSettings, options);
  }
}

export class GlobalSettingsCollectionService<
  V extends ODataVersionV4 = "4.0",
> extends EntitySetServiceV4<
  GlobalSettings,
  EditableGlobalSettings,
  QGlobalSettings,
  GlobalSettingsId,
  GlobalSettingsService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qGlobalSettings, new QGlobalSettingsId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new GlobalSettingsService<V>(client, path, name, options);
  }
}
