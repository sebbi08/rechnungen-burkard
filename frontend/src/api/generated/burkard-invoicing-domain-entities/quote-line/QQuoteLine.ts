import {
  QueryObject,
  QNumberPath,
  QStringPath,
  QBooleanPath,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { QuoteLineId } from "./QuoteLine.js";

export class QQuoteLine extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly QuoteId = new QNumberPath(this.withPrefix("QuoteId"));
  public readonly ItemId = new QNumberPath(this.withPrefix("ItemId"));
  public readonly ItemNumber = new QStringPath(this.withPrefix("ItemNumber"));
  public readonly Description = new QStringPath(this.withPrefix("Description"));
  public readonly Count = new QNumberPath(this.withPrefix("Count"));
  public readonly UnitPrice = new QNumberPath(this.withPrefix("UnitPrice"));
  public readonly VatRate = new QNumberPath(this.withPrefix("VatRate"));
  public readonly PriceIncludesVat = new QBooleanPath(this.withPrefix("PriceIncludesVat"));
  public readonly LineNumber = new QNumberPath(this.withPrefix("LineNumber"));
}

export const qQuoteLine = new QQuoteLine();

export class QQuoteLineId extends QId<QuoteLineId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
