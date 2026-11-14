import type {
  InvoiceItem,
  EditableInvoiceItem,
  InvoiceItemId,
} from "../invoice-item/InvoiceItem.js";

export interface GraveItem {
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
   * | Name | `ItemId` |
   * | Type | `Edm.Int64` |
   * | Nullable | `false` |
   */
  ItemId: number;
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
   */
  UnitPrice: number | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Item` |
   * | Type | `Burkard.Invoicing.Domain.Entities.InvoiceItem` |
   * | Nullable | `false` |
   */
  Item?: InvoiceItem;
}

export type GraveItemId = number | { Id: number };

export interface EditableGraveItem
  extends
    Pick<GraveItem, "GraveId" | "ItemId" | "Count">,
    Partial<Pick<GraveItem, "Id" | "UnitPrice">> {
  /**
   * Create "Item" along with this entity (deep insert), or update it along with it (deep update).
   * Bind "Item" to an already existing entity by its key.
   */
  Item?: EditableInvoiceItem | { "@id": InvoiceItemId | string };
}
