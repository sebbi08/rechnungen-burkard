export interface VatRate {
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
     * | Name | `Rate` |
     * | Type | `Edm.Decimal` |
     * | Nullable | `false` |
     */
    Rate: number;
}

export type VatRateId = number | {Id: number};

export interface EditableVatRate extends Pick<VatRate, "Rate">, Partial<Pick<VatRate, "Id">> {
}
