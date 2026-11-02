import type { GravePosition } from "../grave-position/GravePosition.js";
import type { Cemetery, EditableCemetery, CemeteryId } from "../cemetery/Cemetery.js";
import type { GraveItem, EditableGraveItem, GraveItemId } from "../grave-item/GraveItem.js";
import type {
  GraveRecipient,
  EditableGraveRecipient,
  GraveRecipientId,
} from "../grave-recipient/GraveRecipient.js";

export interface Grave {
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
   * | Name | `GraveNumber` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  GraveNumber: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `FieldNumber` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  FieldNumber: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `SiteName` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  SiteName: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `CemeteryId` |
   * | Type | `Edm.Int64` |
   * | Nullable | `false` |
   */
  CemeteryId: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Position` |
   * | Type | `Burkard.Invoicing.Domain.Entities.GravePosition` |
   */
  Position: GravePosition | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `StartDate` |
   * | Type | `Edm.Date` |
   * | Nullable | `false` |
   */
  StartDate: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `EndDate` |
   * | Type | `Edm.Date` |
   */
  EndDate: string | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `CreatedAt` |
   * | Type | `Edm.Date` |
   * | Nullable | `false` |
   */
  CreatedAt: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Cemetery` |
   * | Type | `Burkard.Invoicing.Domain.Entities.Cemetery` |
   * | Nullable | `false` |
   */
  Cemetery?: Cemetery;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Items` |
   * | Type | `Collection(Burkard.Invoicing.Domain.Entities.GraveItem)` |
   */
  Items?: Array<GraveItem>;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Recipients` |
   * | Type | `Collection(Burkard.Invoicing.Domain.Entities.GraveRecipient)` |
   */
  Recipients?: Array<GraveRecipient>;
}

export type GraveId = number | { Id: number };

export interface EditableGrave
  extends
    Pick<
      Grave,
      "GraveNumber" | "FieldNumber" | "SiteName" | "CemeteryId" | "StartDate" | "CreatedAt"
    >,
    Partial<Pick<Grave, "Id" | "Position" | "EndDate">> {
  /**
   * Create "Cemetery" along with this entity (deep insert), or update it along with it (deep update).
   * Bind "Cemetery" to an already existing entity by its key.
   */
  Cemetery?: EditableCemetery | { "@id": CemeteryId | string };
  /**
   * Create "Items" along with this entity (deep insert), or update it along with it (deep update).
   * Bind "Items" to an already existing entity by its key.
   */
  Items?: Array<EditableGraveItem | { "@id": GraveItemId | string }>;
  /**
   * Create "Recipients" along with this entity (deep insert), or update it along with it (deep update).
   * Bind "Recipients" to an already existing entity by its key.
   */
  Recipients?: Array<EditableGraveRecipient | { "@id": GraveRecipientId | string }>;
}
