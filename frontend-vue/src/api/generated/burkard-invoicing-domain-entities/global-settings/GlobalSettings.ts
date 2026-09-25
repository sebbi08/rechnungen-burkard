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
   * | Name | `Language` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  Language: string;
}

export type GlobalSettingsId = number | { Id: number };

export interface EditableGlobalSettings
  extends
    Pick<GlobalSettings, "EndingText" | "StampWord" | "Language">,
    Partial<Pick<GlobalSettings, "Id">> {}
