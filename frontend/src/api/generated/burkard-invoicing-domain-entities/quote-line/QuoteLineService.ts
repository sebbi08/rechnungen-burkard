import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { QuoteLine, EditableQuoteLine, QuoteLineId } from "./QuoteLine.js";
import type { QQuoteLine } from "./QQuoteLine.js";
import { qQuoteLine, QQuoteLineId } from "./QQuoteLine.js";

export class QuoteLineService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  QuoteLine,
  EditableQuoteLine,
  QQuoteLine,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qQuoteLine, options);
  }
}

export class QuoteLineCollectionService<
  V extends ODataVersionV4 = "4.0",
> extends EntitySetServiceV4<
  QuoteLine,
  EditableQuoteLine,
  QQuoteLine,
  QuoteLineId,
  QuoteLineService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qQuoteLine, new QQuoteLineId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new QuoteLineService<V>(client, path, name, options);
  }
}
