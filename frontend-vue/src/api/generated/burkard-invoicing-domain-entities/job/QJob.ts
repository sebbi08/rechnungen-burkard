import { QueryObject, QNumberPath, QStringPath, QEnumPath, QDateTimeOffsetPath, QId, QNumberParam } from "@odata2ts/odata-query-objects";
import type { JobId } from "./Job.js";
import { JobStatus } from "../job-status/JobStatus.js";

export class QJob extends QueryObject {
    public readonly Id = new QNumberPath(this.withPrefix("Id"));
    public readonly Type = new QStringPath(this.withPrefix("Type"));
    public readonly Status = new QEnumPath(this.withPrefix("Status"), JobStatus);
    public readonly ProgressPercent = new QNumberPath(this.withPrefix("ProgressPercent"));
    public readonly TotalCount = new QNumberPath(this.withPrefix("TotalCount"));
    public readonly ProcessedCount = new QNumberPath(this.withPrefix("ProcessedCount"));
    public readonly PayloadJson = new QStringPath(this.withPrefix("PayloadJson"));
    public readonly ResultJson = new QStringPath(this.withPrefix("ResultJson"));
    public readonly Error = new QStringPath(this.withPrefix("Error"));
    public readonly CreatedAt = new QDateTimeOffsetPath(this.withPrefix("CreatedAt"));
    public readonly StartedAt = new QDateTimeOffsetPath(this.withPrefix("StartedAt"));
    public readonly FinishedAt = new QDateTimeOffsetPath(this.withPrefix("FinishedAt"));
}

export const qJob = new QJob();

export class QJobId extends QId<JobId> {
    private readonly params = [new QNumberParam("Id")];

    getParams() {
        return this.params
    }
}
