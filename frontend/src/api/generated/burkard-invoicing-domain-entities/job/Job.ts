import type { JobStatus } from "../job-status/JobStatus.js";

export interface Job {
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
   * | Name | `Type` |
   * | Type | `Edm.String` |
   * | Nullable | `false` |
   */
  Type: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Status` |
   * | Type | `Burkard.Invoicing.Domain.Entities.JobStatus` |
   * | Nullable | `false` |
   */
  Status: JobStatus;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `ProgressPercent` |
   * | Type | `Edm.Int32` |
   * | Nullable | `false` |
   */
  ProgressPercent: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `TotalCount` |
   * | Type | `Edm.Int32` |
   * | Nullable | `false` |
   */
  TotalCount: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `ProcessedCount` |
   * | Type | `Edm.Int32` |
   * | Nullable | `false` |
   */
  ProcessedCount: number;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `PayloadJson` |
   * | Type | `Edm.String` |
   */
  PayloadJson: string | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `ResultJson` |
   * | Type | `Edm.String` |
   */
  ResultJson: string | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `Error` |
   * | Type | `Edm.String` |
   */
  Error: string | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `CreatedAt` |
   * | Type | `Edm.DateTimeOffset` |
   * | Nullable | `false` |
   */
  CreatedAt: string;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `StartedAt` |
   * | Type | `Edm.DateTimeOffset` |
   */
  StartedAt: string | null;
  /**
   *
   * OData Attributes:
   * |Attribute Name | Attribute Value |
   * | --- | ---|
   * | Name | `FinishedAt` |
   * | Type | `Edm.DateTimeOffset` |
   */
  FinishedAt: string | null;
}

export type JobId = number | { Id: number };

export interface EditableJob
  extends
    Pick<
      Job,
      "Type" | "Status" | "ProgressPercent" | "TotalCount" | "ProcessedCount" | "CreatedAt"
    >,
    Partial<
      Pick<Job, "Id" | "PayloadJson" | "ResultJson" | "Error" | "StartedAt" | "FinishedAt">
    > {}
