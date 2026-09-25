export interface Recipient {
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
     * | Name | `Name` |
     * | Type | `Edm.String` |
     * | Nullable | `false` |
     */
    Name: string;
    /**
     *
     * OData Attributes:
     * |Attribute Name | Attribute Value |
     * | --- | ---|
     * | Name | `Address` |
     * | Type | `Edm.String` |
     * | Nullable | `false` |
     */
    Address: string;
}

export type RecipientId = number | {Id: number};

export interface EditableRecipient extends Pick<Recipient, "Number" | "Name" | "Address">, Partial<Pick<Recipient, "Id">> {
}
