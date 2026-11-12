import {
  QueryObject,
  QNumberPath,
  QBooleanPath,
  QStringPath,
  QEnumPath,
  QDateTimeOffsetPath,
  QEntityCollectionPath,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { QuoteId } from "./Quote.js";
import { QuoteState } from "../quote-state/QuoteState.js";
import { QQuoteLine } from "../quote-line/QQuoteLine.js";

export class QQuote extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly RootId = new QNumberPath(this.withPrefix("RootId"));
  public readonly RevisionNumber = new QNumberPath(this.withPrefix("RevisionNumber"));
  public readonly IsLatestRevision = new QBooleanPath(this.withPrefix("IsLatestRevision"));
  public readonly Year = new QNumberPath(this.withPrefix("Year"));
  public readonly Sequence = new QNumberPath(this.withPrefix("Sequence"));
  public readonly QuoteNumber = new QStringPath(this.withPrefix("QuoteNumber"));
  public readonly State = new QEnumPath(this.withPrefix("State"), QuoteState);
  public readonly InvoiceId = new QNumberPath(this.withPrefix("InvoiceId"));
  public readonly RecipientId = new QNumberPath(this.withPrefix("RecipientId"));
  public readonly RecipientName = new QStringPath(this.withPrefix("RecipientName"));
  public readonly RecipientAddress = new QStringPath(this.withPrefix("RecipientAddress"));
  public readonly RecipientNumber = new QStringPath(this.withPrefix("RecipientNumber"));
  public readonly EndingText = new QStringPath(this.withPrefix("EndingText"));
  public readonly GeneratedAt = new QDateTimeOffsetPath(this.withPrefix("GeneratedAt"));
  public readonly NetTotal = new QNumberPath(this.withPrefix("NetTotal"));
  public readonly VatTotal = new QNumberPath(this.withPrefix("VatTotal"));
  public readonly GrossTotal = new QNumberPath(this.withPrefix("GrossTotal"));
  public readonly InvoiceNumber = new QStringPath(this.withPrefix("InvoiceNumber"));
  public readonly Lines = new QEntityCollectionPath(this.withPrefix("Lines"), () => QQuoteLine);
}

export const qQuote = new QQuote();

export class QQuoteId extends QId<QuoteId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
