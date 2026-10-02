import {
  QueryObject,
  QNumberPath,
  QStringPath,
  QId,
  QNumberParam,
} from "@odata2ts/odata-query-objects";
import type { GlobalSettingsId } from "./GlobalSettings.js";

export class QGlobalSettings extends QueryObject {
  public readonly Id = new QNumberPath(this.withPrefix("Id"));
  public readonly EndingText = new QStringPath(this.withPrefix("EndingText"));
  public readonly StampWord = new QStringPath(this.withPrefix("StampWord"));
}

export const qGlobalSettings = new QGlobalSettings();

export class QGlobalSettingsId extends QId<GlobalSettingsId> {
  private readonly params = [new QNumberParam("Id")];

  getParams() {
    return this.params;
  }
}
