import {
  QueryObject,
  QNumberPath,
  QStringPath,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { InvoiceItemId } from "./InvoiceItem.js";

export class QInvoiceItem extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly Number = new QStringPath(this.withPrefix("Number"));
  public readonly Description = new QStringPath(this.withPrefix("Description"));
  public readonly VatRate = new QNumberPath(this.withPrefix("VatRate"));
  public readonly DefaultPrice = new QNumberPath(this.withPrefix("DefaultPrice"));
}

export const qInvoiceItem = new QInvoiceItem();

export class QInvoiceItemId extends QId<InvoiceItemId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
