import {
  QueryObject,
  QNumberPath,
  QDateTimeOffsetPath,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { GraveInvoiceRunId } from "./GraveInvoiceRun.js";

export class QGraveInvoiceRun extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly GraveId = new QNumberPath(this.withPrefix("GraveId"));
  public readonly Year = new QNumberPath(this.withPrefix("Year"));
  public readonly Half = new QNumberPath(this.withPrefix("Half"));
  public readonly JobId = new QNumberPath(this.withPrefix("JobId"));
  public readonly CreatedAt = new QDateTimeOffsetPath(this.withPrefix("CreatedAt"));
}

export const qGraveInvoiceRun = new QGraveInvoiceRun();

export class QGraveInvoiceRunId extends QId<GraveInvoiceRunId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
