export interface GlobalSettings {
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
   * | Name | `InvoiceEndingText` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  InvoiceEndingText: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `QuoteEndingText` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  QuoteEndingText: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `GraveFirstHalfEndingText` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  GraveFirstHalfEndingText: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `GraveSecondHalfEndingText` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  GraveSecondHalfEndingText: string;
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
}

export type GlobalSettingsId = number | { Id: number };

export interface EditableGlobalSettings
  extends
    Pick<
      GlobalSettings,
      | "InvoiceEndingText"
      | "QuoteEndingText"
      | "GraveFirstHalfEndingText"
      | "GraveSecondHalfEndingText"
      | "StampWord"
    >,
    Partial<Pick<GlobalSettings, "Id">> {}
