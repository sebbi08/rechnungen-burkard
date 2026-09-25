import { QueryObject, QNumberPath, QId, QNumberParam } from "@odata2ts/odata-query-objects";
import type { VatRateId } from "./VatRate.js";

export class QVatRate extends QueryObject {
    public readonly Id = new QNumberPath(this.withPrefix("Id"));
    public readonly Rate = new QNumberPath(this.withPrefix("Rate"));
}

export const qVatRate = new QVatRate();

export class QVatRateId extends QId<VatRateId> {
    private readonly params = [new QNumberParam("Id")];

    getParams() {
        return this.params
    }
}
