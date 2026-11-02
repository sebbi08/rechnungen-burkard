import {
  QueryObject,
  QNumberPath,
  QStringPath,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { CemeteryId } from "./Cemetery.js";

export class QCemetery extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly Name = new QStringPath(this.withPrefix("Name"));
}

export const qCemetery = new QCemetery();

export class QCemeteryId extends QId<CemeteryId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
