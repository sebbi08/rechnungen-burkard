export interface AuditEntry {
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
   * | Name | `OccurredAt` |
   * | Type | `Edm.DateTimeOffset` |
   * | Nullable | `false` |
   */
  OccurredAt: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Action` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  Action: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `DetailsJson` |
   * | Type | `Edm.String` |
   */
  DetailsJson: string | null;
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
   * | Name | `InvoiceRootId` |
   * | Type | `Edm.Int64` |
   */
  InvoiceRootId: number | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `ChangesJson` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  ChangesJson: string;
}

export type AuditEntryId = number | { Id: number };

export interface EditableAuditEntry
  extends
    Pick<AuditEntry, "OccurredAt" | "Action" | "ChangesJson">,
    Partial<Pick<AuditEntry, "Id" | "DetailsJson" | "GraveId" | "InvoiceRootId">> {}
