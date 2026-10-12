import {
  QueryObject,
  QNumberPath,
  QStringPath,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { RecipientId } from "./Recipient.js";

export class QRecipient extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly Number = new QStringPath(this.withPrefix("Number"));
  public readonly Name = new QStringPath(this.withPrefix("Name"));
  public readonly Address = new QStringPath(this.withPrefix("Address"));
  public readonly Email = new QStringPath(this.withPrefix("Email"));
}

export const qRecipient = new QRecipient();

export class QRecipientId extends QId<RecipientId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
