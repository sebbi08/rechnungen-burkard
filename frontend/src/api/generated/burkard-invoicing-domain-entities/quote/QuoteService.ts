import type { ODataVersionV4 } from "@odata2ts/odata-core";
import type { ODataHttpClient } from "@odata2ts/http-client-api";
import {
  EntityTypeServiceV4,
  ODataServiceOptionsInternal,
  EntitySetServiceV4,
} from "@odata2ts/odata-service";
import type { Quote, EditableQuote, QuoteId } from "./Quote.js";
import type { QQuote } from "./QQuote.js";
import type { QuoteLineId } from "../quote-line/QuoteLine.js";
import { qQuote, QQuoteId } from "./QQuote.js";
import { QuoteLineService, QuoteLineCollectionService } from "../quote-line/QuoteLineService.js";

export class QuoteService<V extends ODataVersionV4 = "4.0"> extends EntityTypeServiceV4<
  Quote,
  EditableQuote,
  QQuote,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qQuote, options);
  }

  public Lines(): QuoteLineCollectionService<V>;
  public Lines(id: QuoteLineId): QuoteLineService<V>;
  public Lines(id?: QuoteLineId | undefined) {
    const fieldName = "Lines";
    const { client, path, options } = this.__base;
    const collection = new QuoteLineCollectionService(client, path, fieldName, {
      ...options,
      subtype: false,
    });
    return typeof id === "undefined" || id === null ? collection : collection.byId(id);
  }
}

export class QuoteCollectionService<V extends ODataVersionV4 = "4.0"> extends EntitySetServiceV4<
  Quote,
  EditableQuote,
  QQuote,
  QuoteId,
  QuoteService<V>,
  V
> {
  constructor(
    client: ODataHttpClient,
    basePath: string,
    name: string,
    options?: ODataServiceOptionsInternal<V>,
  ) {
    super(client, basePath, name, qQuote, new QQuoteId(name), options);
  }

  protected createEntityService(
    client: ODataHttpClient,
    path: string,
    name: string,
    options: ODataServiceOptionsInternal<V> | undefined,
  ) {
    return new QuoteService<V>(client, path, name, options);
  }
}
