export interface InvoiceItem {
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
   * | Name | `Number` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  Number: string;
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
   * | Name | `DefaultPrice` |
   * | Type | `Edm.Decimal` |
   */
  DefaultPrice: number | null;
}

export type InvoiceItemId = number | { Id: number };

export interface EditableInvoiceItem
  extends
    Pick<InvoiceItem, "Number" | "Description" | "VatRate">,
    Partial<Pick<InvoiceItem, "Id" | "DefaultPrice">> {}
