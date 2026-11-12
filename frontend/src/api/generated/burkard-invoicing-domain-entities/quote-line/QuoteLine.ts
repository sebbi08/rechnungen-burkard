export interface QuoteLine {
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
   * | Name | `QuoteId` |
   * | Type | `Edm.Int64` |
   * | Nullable | `false` |
   */
  QuoteId: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `ItemId` |
   * | Type | `Edm.Int64` |
   */
  ItemId: number | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `ItemNumber` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  ItemNumber: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Description` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  Description: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Count` |
   * | Type | `Edm.Int32` |
   * | Nullable | `false` |
   */
  Count: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `UnitPrice` |
   * | Type | `Edm.Decimal` |
   * | Nullable | `false` |
   */
  UnitPrice: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `VatRate` |
   * | Type | `Edm.Decimal` |
   * | Nullable | `false` |
   */
  VatRate: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `PriceIncludesVat` |
   * | Type | `Edm.Boolean` |
   * | Nullable | `false` |
   */
  PriceIncludesVat: boolean;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `LineNumber` |
   * | Type | `Edm.Int32` |
   * | Nullable | `false` |
   */
  LineNumber: number;
}

export type QuoteLineId = number | { Id: number };

export interface EditableQuoteLine
  extends
    Pick<
      QuoteLine,
      | "QuoteId"
      | "ItemNumber"
      | "Description"
      | "Count"
      | "UnitPrice"
      | "VatRate"
      | "PriceIncludesVat"
      | "LineNumber"
    >,
    Partial<Pick<QuoteLine, "Id" | "ItemId">> {}
