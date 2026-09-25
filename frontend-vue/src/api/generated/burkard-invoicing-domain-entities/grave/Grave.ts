import type { GraveItem, EditableGraveItem, GraveItemId } from "../grave-item/GraveItem.js";
import type { GraveRecipient, EditableGraveRecipient, GraveRecipientId } from "../grave-recipient/GraveRecipient.js";

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
     * | Nullable | `false` |
     */
    EndDate: string;
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

export type GraveId = number | {Id: number};

export interface EditableGrave extends Pick<Grave, "GraveNumber" | "StartDate" | "EndDate" | "CreatedAt">, Partial<Pick<Grave, "Id">> {
    /**
     * Create "Items" along with this entity (deep insert), or update it along with it (deep update).
     * Bind "Items" to an already existing entity by its key.
     */
    "Items"?: Array<EditableGraveItem | { "@id": GraveItemId | string }>;
    /**
     * Create "Recipients" along with this entity (deep insert), or update it along with it (deep update).
     * Bind "Recipients" to an already existing entity by its key.
     */
    "Recipients"?: Array<EditableGraveRecipient | { "@id": GraveRecipientId | string }>;
}
