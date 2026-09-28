import type { DocumentKind } from "../document-kind/DocumentKind.js";
import type { Invoice, EditableInvoice, InvoiceId } from "../invoice/Invoice.js";

export interface Document {
  /**
   * **Key Property**: This is a key property used to identify the entity.<br/>**Immutable**: This property can be set when creating the entity, but not changed afterwards.
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Id` |
   * | Type | `Edm.Int64` |
   * | Nullable | `false` |
   */
  Id: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `InvoiceId` |
   * | Type | `Edm.Int64` |
   * | Nullable | `false` |
   */
  InvoiceId: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Kind` |
   * | Type | `Burkard.Invoicing.Domain.Entities.DocumentKind` |
   * | Nullable | `false` |
   */
  Kind: DocumentKind;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `FileName` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  FileName: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `GeneratedAt` |
   * | Type | `Edm.DateTimeOffset` |
   * | Nullable | `false` |
   */
  GeneratedAt: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Invoice` |
   * | Type | `Burkard.Invoicing.Domain.Entities.Invoice` |
   * | Nullable | `false` |
   */
  Invoice?: Invoice;
}

export type DocumentId = number | { Id: number };

export interface EditableDocument
  extends
    Pick<Document, "InvoiceId" | "Kind" | "FileName" | "GeneratedAt">,
    Partial<Pick<Document, "Id">> {
  /**
   * Create "Invoice" along with this entity (deep insert), or update it along with it (deep update).
   * Bind "Invoice" to an already existing entity by its key.
   */
  Invoice?: EditableInvoice | { "@id": InvoiceId | string };
}
