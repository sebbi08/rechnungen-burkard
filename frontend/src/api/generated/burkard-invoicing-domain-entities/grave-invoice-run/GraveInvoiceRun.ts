export interface GraveInvoiceRun {
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
   * | Name | `GraveId` |
   * | Type | `Edm.Int64` |
   * | Nullable | `false` |
   */
  GraveId: number;
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
   * | Name | `Half` |
   * | Type | `Edm.Int32` |
   * | Nullable | `false` |
   */
  Half: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `JobId` |
   * | Type | `Edm.Int64` |
   */
  JobId: number | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `CreatedAt` |
   * | Type | `Edm.DateTimeOffset` |
   * | Nullable | `false` |
   */
  CreatedAt: string;
}

export type GraveInvoiceRunId = number | { Id: number };

export interface EditableGraveInvoiceRun
  extends
    Pick<GraveInvoiceRun, "GraveId" | "Year" | "Half" | "CreatedAt">,
    Partial<Pick<GraveInvoiceRun, "Id" | "JobId">> {}
