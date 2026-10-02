import {
  QueryObject,
  QNumberPath,
  QStringPath,
  QEnumPath,
  QDatePath,
  QEntityCollectionPath,
  QBinding,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { GraveId } from "./Grave.js";
import { GravePosition } from "../grave-position/GravePosition.js";
import { QGraveItem, QGraveItemId } from "../grave-item/QGraveItem.js";
import { QGraveRecipient, QGraveRecipientId } from "../grave-recipient/QGraveRecipient.js";

export class QGrave extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly GraveNumber = new QStringPath(this.withPrefix("GraveNumber"));
  public readonly FieldNumber = new QStringPath(this.withPrefix("FieldNumber"));
  public readonly SiteName = new QStringPath(this.withPrefix("SiteName"));
  public readonly Position = new QEnumPath(this.withPrefix("Position"), GravePosition);
  public readonly StartDate = new QDatePath(this.withPrefix("StartDate"));
  public readonly EndDate = new QDatePath(this.withPrefix("EndDate"));
  public readonly CreatedAt = new QDatePath(this.withPrefix("CreatedAt"));
  public readonly Items = new QEntityCollectionPath(
    this.withPrefix("Items"),
    () => QGraveItem,
    new QBinding(() => new QGraveItemId("GraveItems"), "4.0"),
  );
  public readonly Recipients = new QEntityCollectionPath(
    this.withPrefix("Recipients"),
    () => QGraveRecipient,
    new QBinding(() => new QGraveRecipientId("GraveRecipients"), "4.0"),
  );
}

export const qGrave = new QGrave();

export class QGraveId extends QId<GraveId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
