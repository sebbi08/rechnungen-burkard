import {
  QueryObject,
  QNumberPath,
  QStringPath,
  QEnumPath,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { InvoiceItemId } from "./InvoiceItem.js";
import { BillingQuarter } from "../billing-quarter/BillingQuarter.js";

export class QInvoiceItem extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly Number = new QStringPath(this.withPrefix("Number"));
  public readonly Description = new QStringPath(this.withPrefix("Description"));
  public readonly VatRate = new QNumberPath(this.withPrefix("VatRate"));
  public readonly DefaultPrice = new QNumberPath(this.withPrefix("DefaultPrice"));
  public readonly Quarter = new QEnumPath(this.withPrefix("Quarter"), BillingQuarter);
}

export const qInvoiceItem = new QInvoiceItem();

export class QInvoiceItemId extends QId<InvoiceItemId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
