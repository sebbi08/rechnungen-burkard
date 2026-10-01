import {
  QueryObject,
  QNumberPath,
  QBooleanPath,
  QStringPath,
  QDateTimeOffsetPath,
  QDatePath,
  QEntityCollectionPath,
  QBinding,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { InvoiceId } from "./Invoice.js";
import { QInvoiceLine } from "../invoice-line/QInvoiceLine.js";
import { QDocument, QDocumentId } from "../document/QDocument.js";

export class QInvoice extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly RootId = new QNumberPath(this.withPrefix("RootId"));
  public readonly RevisionNumber = new QNumberPath(this.withPrefix("RevisionNumber"));
  public readonly IsLatestRevision = new QBooleanPath(this.withPrefix("IsLatestRevision"));
  public readonly Year = new QNumberPath(this.withPrefix("Year"));
  public readonly Sequence = new QNumberPath(this.withPrefix("Sequence"));
  public readonly InvoiceNumber = new QStringPath(this.withPrefix("InvoiceNumber"));
  public readonly GraveId = new QNumberPath(this.withPrefix("GraveId"));
  public readonly PeriodYear = new QNumberPath(this.withPrefix("PeriodYear"));
  public readonly PeriodHalf = new QNumberPath(this.withPrefix("PeriodHalf"));
  public readonly SharePercent = new QNumberPath(this.withPrefix("SharePercent"));
  public readonly RecipientCount = new QNumberPath(this.withPrefix("RecipientCount"));
  public readonly RecipientId = new QNumberPath(this.withPrefix("RecipientId"));
  public readonly RecipientName = new QStringPath(this.withPrefix("RecipientName"));
  public readonly RecipientAddress = new QStringPath(this.withPrefix("RecipientAddress"));
  public readonly EndingText = new QStringPath(this.withPrefix("EndingText"));
  public readonly StampWord = new QStringPath(this.withPrefix("StampWord"));
  public readonly Language = new QStringPath(this.withPrefix("Language"));
  public readonly GeneratedAt = new QDateTimeOffsetPath(this.withPrefix("GeneratedAt"));
  public readonly PaidOn = new QDatePath(this.withPrefix("PaidOn"));
  public readonly NetTotal = new QNumberPath(this.withPrefix("NetTotal"));
  public readonly VatTotal = new QNumberPath(this.withPrefix("VatTotal"));
  public readonly GrossTotal = new QNumberPath(this.withPrefix("GrossTotal"));
  public readonly Lines = new QEntityCollectionPath(this.withPrefix("Lines"), () => QInvoiceLine);
  public readonly Documents = new QEntityCollectionPath(
    this.withPrefix("Documents"),
    () => QDocument,
    new QBinding(() => new QDocumentId("Documents"), "4.0"),
  );
}

export const qInvoice = new QInvoice();

export class QInvoiceId extends QId<InvoiceId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
