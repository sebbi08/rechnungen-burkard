import { QueryObject, QNumberPath, QEnumPath, QStringPath, QDateTimeOffsetPath, QEntityPath, QBinding, QId, QNumberParam } from "@odata2ts/odata-query-objects";
import type { DocumentId } from "./Document.js";
import { DocumentKind } from "../document-kind/DocumentKind.js";
import { QInvoice, QInvoiceId } from "../invoice/QInvoice.js";

export class QDocument extends QueryObject {
    public readonly Id = new QNumberPath(this.withPrefix("Id"));
    public readonly InvoiceId = new QNumberPath(this.withPrefix("InvoiceId"));
    public readonly Kind = new QEnumPath(this.withPrefix("Kind"), DocumentKind);
    public readonly FileName = new QStringPath(this.withPrefix("FileName"));
    public readonly GeneratedAt = new QDateTimeOffsetPath(this.withPrefix("GeneratedAt"));
    public readonly Invoice = new QEntityPath(this.withPrefix("Invoice"), () => QInvoice, new QBinding(() => new QInvoiceId("Invoices"), "4.0"));
}

export const qDocument = new QDocument();

export class QDocumentId extends QId<DocumentId> {
    private readonly params = [new QNumberParam("Id")];

    getParams() {
        return this.params
    }
}
