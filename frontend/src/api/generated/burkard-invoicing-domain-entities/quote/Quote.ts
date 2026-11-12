import type { QuoteState } from "../quote-state/QuoteState.js";
import type { QuoteLine, EditableQuoteLine } from "../quote-line/QuoteLine.js";

export interface Quote {
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
   * | Name | `QuoteNumber` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  QuoteNumber: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `State` |
   * | Type | `Burkard.Invoicing.Domain.Entities.QuoteState` |
   * | Nullable | `false` |
   */
  State: QuoteState;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `InvoiceId` |
   * | Type | `Edm.Int64` |
   */
  InvoiceId: number | null;
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
   * | Name | `RecipientNumber` |
   * | Type | `Edm.String` |
   */
  RecipientNumber: string | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `IntroText` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  IntroText: string;
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
   * | Name | `InvoiceNumber` |
   * | Type | `Edm.String` |
   */
  InvoiceNumber: string | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Lines` |
   * | Type | `Collection(Burkard.Invoicing.Domain.Entities.QuoteLine)` |
   */
  Lines?: Array<QuoteLine>;
}

export type QuoteId = number | { Id: number };

export interface EditableQuote
  extends
    Pick<
      Quote,
      | "RootId"
      | "RevisionNumber"
      | "IsLatestRevision"
      | "Year"
      | "Sequence"
      | "QuoteNumber"
      | "State"
      | "RecipientId"
      | "RecipientName"
      | "RecipientAddress"
      | "IntroText"
      | "EndingText"
      | "GeneratedAt"
      | "NetTotal"
      | "VatTotal"
      | "GrossTotal"
    >,
    Partial<Pick<Quote, "Id" | "InvoiceId" | "RecipientNumber" | "InvoiceNumber">> {
  /** Create "Lines" along with this entity (deep insert), or update it along with it (deep update). */
  Lines?: Array<EditableQuoteLine>;
}
