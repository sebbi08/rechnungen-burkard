import {
  QueryObject,
  QNumberPath,
  QDateTimeOffsetPath,
  QStringPath,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { AuditEntryId } from "./AuditEntry.js";

export class QAuditEntry extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly OccurredAt = new QDateTimeOffsetPath(this.withPrefix("OccurredAt"));
  public readonly Action = new QStringPath(this.withPrefix("Action"));
  public readonly DetailsJson = new QStringPath(this.withPrefix("DetailsJson"));
  public readonly GraveId = new QNumberPath(this.withPrefix("GraveId"));
  public readonly InvoiceRootId = new QNumberPath(this.withPrefix("InvoiceRootId"));
  public readonly ChangesJson = new QStringPath(this.withPrefix("ChangesJson"));
}

export const qAuditEntry = new QAuditEntry();

export class QAuditEntryId extends QId<AuditEntryId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
