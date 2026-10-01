import {
  QueryObject,
  QNumberPath,
  QBooleanPath,
  QEntityPath,
  QBinding,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { GraveItemId } from "./GraveItem.js";
import { QInvoiceItem, QInvoiceItemId } from "../invoice-item/QInvoiceItem.js";

export class QGraveItem extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly GraveId = new QNumberPath(this.withPrefix("GraveId"));
  public readonly ItemId = new QNumberPath(this.withPrefix("ItemId"));
  public readonly Count = new QNumberPath(this.withPrefix("Count"));
  public readonly UnitPrice = new QNumberPath(this.withPrefix("UnitPrice"));
  public readonly PriceIncludesVat = new QBooleanPath(this.withPrefix("PriceIncludesVat"));
  public readonly Item = new QEntityPath(
    this.withPrefix("Item"),
    () => QInvoiceItem,
    new QBinding(() => new QInvoiceItemId("InvoiceItems"), "4.0"),
  );
}

export const qGraveItem = new QGraveItem();

export class QGraveItemId extends QId<GraveItemId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
