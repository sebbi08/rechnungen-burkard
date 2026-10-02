import type { Recipient, EditableRecipient, RecipientId } from "../recipient/Recipient.js";

export interface GraveRecipient {
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
   * | Name | `Recipient` |
   * | Type | `Burkard.Invoicing.Domain.Entities.Recipient` |
   * | Nullable | `false` |
   */
  Recipient?: Recipient;
}

export type GraveRecipientId = number | { Id: number };

export interface EditableGraveRecipient
  extends Pick<GraveRecipient, "GraveId" | "RecipientId">, Partial<Pick<GraveRecipient, "Id">> {
  /**
   * Create "Recipient" along with this entity (deep insert), or update it along with it (deep update).
   * Bind "Recipient" to an already existing entity by its key.
   */
  Recipient?: EditableRecipient | { "@id": RecipientId | string };
}
