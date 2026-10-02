import type { InvoiceLine, EditableInvoiceLine } from "../invoice-line/InvoiceLine.js";
import type { Document, EditableDocument, DocumentId } from "../document/Document.js";

export interface Invoice {
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
   * | Name | `RootId` |
   * | Type | `Edm.Int64` |
   * | Nullable | `false` |
   */
  RootId: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `RevisionNumber` |
   * | Type | `Edm.Int32` |
   * | Nullable | `false` |
   */
  RevisionNumber: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `IsLatestRevision` |
   * | Type | `Edm.Boolean` |
   * | Nullable | `false` |
   */
  IsLatestRevision: boolean;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Year` |
   * | Type | `Edm.Int32` |
   * | Nullable | `false` |
   */
  Year: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Sequence` |
   * | Type | `Edm.Int32` |
   * | Nullable | `false` |
   */
  Sequence: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `InvoiceNumber` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  InvoiceNumber: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `GraveId` |
   * | Type | `Edm.Int64` |
   */
  GraveId: number | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `PeriodYear` |
   * | Type | `Edm.Int32` |
   */
  PeriodYear: number | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `PeriodHalf` |
   * | Type | `Edm.Int32` |
   */
  PeriodHalf: number | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `SharePercent` |
   * | Type | `Edm.Decimal` |
   */
  SharePercent: number | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `RecipientCount` |
   * | Type | `Edm.Int32` |
   */
  RecipientCount: number | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `RecipientId` |
   * | Type | `Edm.Int64` |
   * | Nullable | `false` |
   */
  RecipientId: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `RecipientName` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  RecipientName: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `RecipientAddress` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  RecipientAddress: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `EndingText` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  EndingText: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `StampWord` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  StampWord: string;
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
   * | Name | `PaidOn` |
   * | Type | `Edm.Date` |
   */
  PaidOn: string | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `NetTotal` |
   * | Type | `Edm.Decimal` |
   * | Nullable | `false` |
   */
  NetTotal: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `VatTotal` |
   * | Type | `Edm.Decimal` |
   * | Nullable | `false` |
   */
  VatTotal: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `GrossTotal` |
   * | Type | `Edm.Decimal` |
   * | Nullable | `false` |
   */
  GrossTotal: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Lines` |
   * | Type | `Collection(Burkard.Invoicing.Domain.Entities.InvoiceLine)` |
   */
  Lines?: Array<InvoiceLine>;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Documents` |
   * | Type | `Collection(Burkard.Invoicing.Domain.Entities.Document)` |
   */
  Documents?: Array<Document>;
}

export type InvoiceId = number | { Id: number };

export interface EditableInvoice
  extends
    Pick<
      Invoice,
      | "RootId"
      | "RevisionNumber"
      | "IsLatestRevision"
      | "Year"
      | "Sequence"
      | "InvoiceNumber"
      | "RecipientId"
      | "RecipientName"
      | "RecipientAddress"
      | "EndingText"
      | "StampWord"
      | "GeneratedAt"
      | "NetTotal"
      | "VatTotal"
      | "GrossTotal"
    >,
    Partial<
      Pick<
        Invoice,
        | "Id"
        | "GraveId"
        | "PeriodYear"
        | "PeriodHalf"
        | "SharePercent"
        | "RecipientCount"
        | "PaidOn"
      >
    > {
  /** Create "Lines" along with this entity (deep insert), or update it along with it (deep update). */
  Lines?: Array<EditableInvoiceLine>;
  /**
   * Create "Documents" along with this entity (deep insert), or update it along with it (deep update).
   * Bind "Documents" to an already existing entity by its key.
   */
  Documents?: Array<EditableDocument | { "@id": DocumentId | string }>;
}
