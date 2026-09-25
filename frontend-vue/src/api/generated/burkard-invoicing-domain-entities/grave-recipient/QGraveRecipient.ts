import {
  QueryObject,
  QNumberPath,
  QEntityPath,
  QBinding,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { GraveRecipientId } from "./GraveRecipient.js";
import { QRecipient, QRecipientId } from "../recipient/QRecipient.js";

export class QGraveRecipient extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly GraveId = new QNumberPath(this.withPrefix("GraveId"));
  public readonly RecipientId = new QNumberPath(this.withPrefix("RecipientId"));
  public readonly Recipient = new QEntityPath(
    this.withPrefix("Recipient"),
    () => QRecipient,
    new QBinding(() => new QRecipientId("Recipients"), "4.0"),
  );
}

export const qGraveRecipient = new QGraveRecipient();

export class QGraveRecipientId extends QId<GraveRecipientId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
