/**
 * Everything the EDM does not describe (D-001), and the actions whose
 * parameters $metadata leaves out, typed by hand after the backend's
 * controllers.
 */
import type { GravePositionName } from "@/lib/gravePosition";
import type { QuarterName } from "@/lib/quarter";
import { type InvoiceLine, type Job, JobStatus, type QuoteLine } from "./generated";
import { ApiError, getBlob, getJson, isTransient, postBlob, postJson, request } from "./http";
import { odata } from "./odata";

// ---- invoices -------------------------------------------------------------

export interface InvoiceLineInput {
  itemId: number;
  count: number;
  unitPrice: number | null;
  priceIncludesVat: boolean;
}

export async function createInvoice(recipientId: number, lines: InvoiceLineInput[]): Promise<void> {
  await postJson("/odata/CreateInvoice", { recipientId, lines });
}

export async function createRevision(
  invoiceId: number,
  recipientId: number,
  lines: InvoiceLineInput[],
): Promise<void> {
  await postJson("/odata/CreateRevision", { invoiceId, recipientId, lines });
}

/** Revises every recipient invoice of the grave and half-year the invoice
 *  belongs to. `lines` are grave-level: the full count at the full price. */
export async function createGraveInvoiceRevision(
  invoiceId: number,
  lines: InvoiceLineInput[],
): Promise<void> {
  await postJson("/odata/CreateGraveInvoiceRevision", { invoiceId, lines });
}

/** Marks an invoice paid -- the backend stamps the day -- or takes the mark back. */
export async function setInvoicePaid(invoiceId: number, paid: boolean): Promise<void> {
  await postJson("/odata/SetInvoicePaid", { invoiceId, paid });
}

export async function fetchInvoiceLines(invoiceId: number): Promise<InvoiceLine[]> {
  return (await getJson<{ value: InvoiceLine[] }>(`/odata/Invoices(${invoiceId})/Lines`)).value;
}

export type DocumentKindName = "Normal" | "Copy";

export interface DocumentRow {
  Id: number;
  FileName: string;
  Kind: DocumentKindName;
}

export async function fetchDocuments(invoiceId: number): Promise<DocumentRow[]> {
  // the PDF bytes are not part of the EDM entity; select keeps the row small
  const url = `/odata/Invoices(${invoiceId})/Documents?$select=Id,FileName,Kind`;
  return (await getJson<{ value: DocumentRow[] }>(url)).value;
}

/** The backend serves the PDF inline, so the browser displays it. */
export function documentUrl(documentId: number): string {
  return `/odata/Documents(${documentId})/$value`;
}

/** The latest-revision Normal PDFs of a half-year's grave invoices, as a ZIP. */
export function bulkDownload(kind: DocumentKindName, year: number, half: number): Promise<Blob> {
  return postBlob("/odata/BulkDownloadDocuments", { kind, year, half });
}

// ---- quotes ---------------------------------------------------------------

export async function createQuote(recipientId: number, lines: InvoiceLineInput[]): Promise<void> {
  await postJson("/odata/CreateQuote", { recipientId, lines });
}

/** A new version of the quote; any row id of its chain addresses it. */
export async function createQuoteRevision(
  quoteId: number,
  recipientId: number,
  lines: InvoiceLineInput[],
): Promise<void> {
  await postJson("/odata/CreateQuoteRevision", { quoteId, recipientId, lines });
}

export async function fetchQuoteLines(quoteId: number): Promise<QuoteLine[]> {
  return (await getJson<{ value: QuoteLine[] }>(`/odata/Quotes(${quoteId})/Lines`)).value;
}

export async function declineQuote(quoteId: number): Promise<void> {
  await postJson("/odata/DeclineQuote", { quoteId });
}

export async function reopenQuote(quoteId: number): Promise<void> {
  await postJson("/odata/ReopenQuote", { quoteId });
}

/** Removes the quote with every version; refused once it is converted. */
export async function deleteQuote(quoteId: number): Promise<void> {
  await postJson("/odata/DeleteQuote", { quoteId });
}

/** Creates the invoice from the quote's latest version and answers its id,
 *  which is also its root id. */
export async function convertQuote(quoteId: number): Promise<number> {
  const invoice = await postJson<{ Id: number }>("/odata/ConvertQuote", { quoteId });
  return Number(invoice.Id);
}

/** The version's one PDF, served inline so the browser displays it. */
export function quotePdfUrl(quoteId: number): string {
  return `/odata/Quotes(${quoteId})/Pdf`;
}

// ---- recipients and items -------------------------------------------------

/** POST through fetch rather than the generated client: an empty number is
 *  left out so the backend assigns one, which the generated create type (Number
 *  required) cannot express. */
export async function createRecipient(input: {
  Number?: string;
  Name: string;
  Address: string;
  Email?: string | null;
}): Promise<number> {
  const body = await postJson<{ Id?: number; id?: number } | undefined>("/odata/Recipients", input);
  return Number(body?.Id ?? body?.id);
}

/** The graves billing an item at one price: the link's own, or the item's
 *  default standing in for it. */
export interface ItemGraveGroup {
  UnitPrice: number | null;
  PriceIncludesVat: boolean;
  IsDefault: boolean;
  Graves: { Id: number; GraveNumber: string; IsCooperative: boolean }[];
}

export interface ItemGraves {
  /** Whether any grave bills the item, which fixes its VAT rate. */
  Linked: boolean;
  DefaultPrice: number | null;
  Groups: ItemGraveGroup[];
}

export function getItemGraves(itemId: number): Promise<ItemGraves> {
  return getJson<ItemGraves>(`/odata/ItemGraves?itemId=${itemId}`);
}

// ---- graves ---------------------------------------------------------------

/** One of a grave's recipients, as the overview lists and searches them. */
export interface GraveStatusRecipient {
  Id: number;
  Name: string;
  Address: string;
}

export interface GraveStatusRow {
  Id: number;
  GraveNumber: string;
  FieldNumber: string;
  SiteName: string;
  CemeteryId: number;
  CemeteryName: string;
  StartDate: string;
  EndDate: string | null;
  Invoiced: boolean;
  InvoiceCount: number;
  PaidCount: number;
  /** In the order they were linked; empty for a grave without any. */
  Recipients: GraveStatusRecipient[];
}

export async function fetchGraveStatus(year: number, half: number): Promise<GraveStatusRow[]> {
  const url = `/odata/GraveInvoicingStatus?year=${year}&half=${half}`;
  return (await getJson<{ value: GraveStatusRow[] }>(url)).value;
}

/** One cooperative grave in the cooperative list. */
export interface CooperativeGraveRow {
  Id: number;
  GraveNumber: string;
  FieldNumber: string;
  SiteName: string;
  CemeteryId: number;
  CemeteryName: string;
  ContractNumber: string | null;
  StartDate: string;
  EndDate: string | null;
  /** When the grave was deleted; null for one still in use. */
  DeletedAt: string | null;
  Recipients: GraveStatusRecipient[];
}

interface CooperativeGraveAnswer {
  Id: number;
  GraveNumber: string;
  FieldNumber: string;
  SiteName: string;
  CemeteryId: number;
  Cemetery: { Id: number; Name: string };
  ContractNumber: string | null;
  StartDate: string;
  EndDate: string | null;
  DeletedAt: string | null;
  /** The links, each with its own id: the order they were made in. */
  Recipients: { Id: number; Recipient: GraveStatusRecipient }[];
}

export async function fetchCooperativeGraves(): Promise<CooperativeGraveRow[]> {
  // No nested "$orderby=Id" for the recipients: it needs a ";" inside
  // $expand, and the proxy in front of the server splits the query at a ";"
  // as at "&", which cut the request in two and left the list empty. The
  // links come with their ids, so they are put in link order here instead.
  const url =
    "/odata/Graves?$filter=IsCooperative eq true&$expand=Cemetery,Recipients($expand=Recipient)&$orderby=GraveNumber";
  const { value } = await getJson<{ value: CooperativeGraveAnswer[] }>(url);
  return value.map((grave) => ({
    Id: grave.Id,
    GraveNumber: grave.GraveNumber,
    FieldNumber: grave.FieldNumber,
    SiteName: grave.SiteName,
    CemeteryId: grave.CemeteryId,
    CemeteryName: grave.Cemetery.Name,
    ContractNumber: grave.ContractNumber,
    StartDate: grave.StartDate,
    EndDate: grave.EndDate,
    DeletedAt: grave.DeletedAt,
    Recipients: [...grave.Recipients]
      .sort((a, b) => a.Id - b.Id)
      .map(({ Recipient }) => ({
        Id: Recipient.Id,
        Name: Recipient.Name,
        Address: Recipient.Address,
      })),
  }));
}

/** One grave that has ended or was deleted, for the page of inactive graves. */
export interface InactiveGraveRow {
  Id: number;
  GraveNumber: string;
  SiteName: string;
  CemeteryName: string;
  StartDate: string;
  EndDate: string | null;
  IsCooperative: boolean;
  ContractNumber: string | null;
  /** When the grave was deleted; null for one that has only ended. */
  DeletedAt: string | null;
}

/** The graves with an end or a deletion; the page cuts the ended ones at the
 *  current half-year itself, as the other lists do. */
export async function fetchInactiveGraves(): Promise<InactiveGraveRow[]> {
  const url =
    "/odata/Graves?$filter=DeletedAt ne null or EndDate ne null&$expand=Cemetery&$orderby=GraveNumber";
  const { value } = await getJson<{
    value: (Omit<InactiveGraveRow, "CemeteryName"> & { Cemetery: { Name: string } })[];
  }>(url);
  return value.map((grave) => ({
    Id: grave.Id,
    GraveNumber: grave.GraveNumber,
    SiteName: grave.SiteName,
    CemeteryName: grave.Cemetery.Name,
    StartDate: grave.StartDate,
    EndDate: grave.EndDate,
    IsCooperative: grave.IsCooperative,
    ContractNumber: grave.ContractNumber,
    DeletedAt: grave.DeletedAt,
  }));
}

export interface SaveGraveInput {
  graveId: number | null;
  graveNumber: string;
  fieldNumber: string;
  siteName: string;
  cemeteryId: number | null;
  position: GravePositionName | null;
  /** "yyyy-MM" */
  startDate: string | null;
  /** "yyyy-MM"; null for a grave without an end */
  endDate: string | null;
  items: InvoiceLineInput[];
  recipientIds: number[];
  /** A cooperative grave is invoiced by the cooperative, not by the business. */
  isCooperative: boolean;
  /** Required for a cooperative grave, null for any other. */
  contractNumber: string | null;
  /** The grave this one takes over from; only when graveId is null. */
  succeedsGraveId?: number | null;
  /** The rounding corrections the user confirmed; the save refuses when they
   *  differ from the ones it computes itself. */
  corrections: {
    kind: string;
    year: number | null;
    half: number;
    vatRate: number;
    itemId: number;
    correctedPrice: number;
  }[];
}

/** The grave and its complete item and recipient links, in one request. */
export async function saveGrave(input: SaveGraveInput): Promise<number> {
  const body = await postJson<{ Id?: number; id?: number } | undefined>("/odata/SaveGrave", input);
  return Number(body?.Id ?? body?.id);
}

/** Deletes a grave; it can be restored until the next invoice run. */
export async function deleteGrave(graveId: number): Promise<void> {
  await postJson("/odata/DeleteGrave", { graveId });
}

export async function restoreGrave(graveId: number): Promise<void> {
  await postJson("/odata/RestoreGrave", { graveId });
}

export interface GraveItemLink {
  Count: number;
  /** The link's own price, stored as typed; null bills the item's default. */
  UnitPrice: number | null;
  PriceIncludesVat: boolean;
  Item: {
    Id: number;
    Number: string;
    Description: string;
    VatRate: number;
    DefaultPrice: number | null;
    Quarter: QuarterName | null;
  };
}

export interface GraveDetails {
  Id: number;
  GraveNumber: string;
  FieldNumber: string;
  SiteName: string;
  CemeteryId: number;
  Cemetery: { Id: number; Name: string };
  Position: GravePositionName | null;
  StartDate: string;
  EndDate: string | null;
  CreatedAt: string;
  IsCooperative: boolean;
  /** The cooperative's number for the grave; null for an ordinary grave. */
  ContractNumber: string | null;
  /** Whether any invoice has been made for the grave. */
  HasInvoices: boolean;
  /** When the grave was deleted, as UTC without a trailing "Z": show the day
   *  only. Null for a grave still in use. */
  DeletedAt: string | null;
  /** Whether a deleted grave can still be restored: no invoice run since. */
  CanRestore: boolean;
  Items: GraveItemLink[];
  Recipients: {
    Recipient: { Id: number; Name: string; Address: string; Email: string | null };
  }[];
}

/** Graves(id) answers hand-built JSON with the links already expanded. */
export function fetchGraveDetails(graveId: number): Promise<GraveDetails> {
  return getJson<GraveDetails>(
    `/odata/Graves(${graveId})?$expand=Cemetery,Items($expand=Item),Recipients($expand=Recipient)`,
  );
}

export interface GraveInvoiceRecipient {
  Id: number;
  Name: string;
  InvoiceId: number;
  Paid: boolean;
  RootId: number;
}

export interface GraveInvoiceGroupLine {
  ItemId: number;
  ItemNumber: string;
  Description: string;
  Count: number;
  UnitPrice: number;
  VatRate: number;
  PriceIncludesVat: boolean;
}

/** The grave, half-year and recipients a grave invoice belongs to, with the
 *  grave's lines as they were invoiced. */
export interface GraveInvoiceGroup {
  GraveId: number;
  GraveNumber: string;
  PeriodYear: number;
  PeriodHalf: number;
  Recipients: GraveInvoiceRecipient[];
  Lines: GraveInvoiceGroupLine[];
}

export function fetchGraveInvoiceGroup(invoiceId: number): Promise<GraveInvoiceGroup> {
  return getJson<GraveInvoiceGroup>(`/odata/GraveInvoiceGroup?invoiceId=${invoiceId}`);
}

export interface GraveBillingProblem {
  VatRate: number;
  Kind: "NotDivisible" | "DoesNotAddUp";
  /** What this rate comes to, in the unit its prices were entered in. */
  Amount: number;
  /** The nearest amounts that would work, in that same unit. */
  Suggestions: number[];
}

export interface GraveBillingCheck {
  Ok: boolean;
  GrossTotal: number | null;
  Problems: GraveBillingProblem[];
}

/** Whether the grave's prices split evenly across its recipients. */
export function checkGraveBilling(
  recipients: number,
  lines: InvoiceLineInput[],
): Promise<GraveBillingCheck> {
  return postJson<GraveBillingCheck>("/odata/ValidateGraveBilling", {
    Recipients: recipients,
    Lines: lines.map((line) => ({
      ItemId: line.itemId,
      Count: line.count,
      UnitPrice: line.unitPrice,
      PriceIncludesVat: line.priceIncludesVat,
    })),
  });
}

export type BillingExampleKind =
  "Start" | "StartAndEnd" | "FullFirstHalf" | "FullSecondHalf" | "End";

export interface BillingPreviewLine {
  LineIndex: number;
  ItemNumber: string;
  /** With ", anteilig …" where the invoice is prorated. */
  Description: string;
  Count: number;
  VatRate: number;
  PriceIncludesVat: boolean;
  /** As billed, corrected; in the unit it was entered in: the gross on a
   *  VAT-included line. */
  Price: number;
  /** What was entered, before a correction. */
  EnteredPrice: number;
  Corrected: boolean;
  UnitPrice: number;
  NetTotal: number;
}

/** One line of an invoice block raised so the block splits evenly. */
export interface BillingCorrection {
  Kind: BillingExampleKind;
  Year: number | null;
  Half: 1 | 2;
  LineIndex: number;
  VatRate: number;
  ItemId: number;
  ItemNumber: string;
  EnteredPrice: number;
  CorrectedPrice: number;
}

export interface BillingPreviewInvoice {
  Kind: BillingExampleKind;
  /** Null for the generic full half-years. */
  Year: number | null;
  Half: 1 | 2;
  Months: number;
  Ok: boolean;
  NetTotal: number;
  /** What the recipients collect between them; null when it does not divide. */
  GrossTotal: number | null;
  PerRecipient: number | null;
  Problems: GraveBillingProblem[];
  Lines: BillingPreviewLine[];
  Corrections: BillingCorrection[];
}

export interface BillingPreview {
  Ok: boolean;
  Recipients: number;
  Invoices: BillingPreviewInvoice[];
}

export interface PreviewRequest {
  recipients: number;
  /** "yyyy-MM" */
  startMonth: string | null;
  /** "yyyy-MM"; null for a grave without an end */
  endMonth: string | null;
  lines: InvoiceLineInput[];
}

/** Every invoice the grave's term will produce, checked like the save checks
 *  them, with the rounding corrections that make them divide. */
export function previewGraveBilling(input: PreviewRequest): Promise<BillingPreview> {
  return postJson<BillingPreview>("/odata/PreviewGraveBilling", {
    Recipients: input.recipients,
    StartMonth: input.startMonth,
    EndMonth: input.endMonth,
    Lines: input.lines.map((line) => ({
      ItemId: line.itemId,
      Count: line.count,
      UnitPrice: line.unitPrice,
      PriceIncludesVat: line.priceIncludesVat,
    })),
  });
}

// ---- generation -----------------------------------------------------------

export interface StartedJob {
  Id: number;
  Status: string;
  ProgressPercent: number;
}

export function generateGraveInvoices(year: number, half: number): Promise<StartedJob> {
  return postJson<StartedJob>("/odata/GenerateGraveInvoices", { year, half });
}

export const JOB_TIMEOUT_MS = 10 * 60 * 1000;

/** How many looks in a row may go unanswered before the wait gives up: at one
 *  a second, long enough for the server to restart under the page. */
const MAX_LOST_POLLS = 30;

/** Polls the job once a second until it succeeds or fails; throws
 *  ApiError("JobTimeout") after ten minutes. A look that got no answer is
 *  tried again rather than ending the wait -- the job runs on in the server
 *  either way, and one dropped answer used to close the overlay on it, show
 *  an error and lose its result. Only a run of them gives up, with the code
 *  of the last. */
export async function pollJob(
  jobId: number,
  onProgress: (percent: number) => void,
  timeoutMs = JOB_TIMEOUT_MS,
): Promise<Job> {
  const deadline = Date.now() + timeoutMs;
  let lost = 0;
  for (;;) {
    if (Date.now() > deadline) {
      throw new ApiError("JobTimeout");
    }
    let job: Job;
    try {
      job = (await odata.Jobs(jobId).query().execute()).data;
      lost = 0;
    } catch (error) {
      if (!isTransient(error) || ++lost > MAX_LOST_POLLS) {
        throw error;
      }
      await new Promise((resolve) => setTimeout(resolve, 1000));
      continue;
    }
    if (job.Status === JobStatus.Succeeded || job.Status === JobStatus.Failed) {
      return job;
    }
    onProgress(job.ProgressPercent ?? 0);
    await new Promise((resolve) => setTimeout(resolve, 1000));
  }
}

// ---- import / export ------------------------------------------------------

export interface ImportProblem {
  file: string;
  row: number | null;
  code: string;
  /** English; the fallback when the key below is unknown. */
  message: string;
  /** Which sentence it is, and its values as written in the archive. */
  messageKey?: string | null;
  messageArgs?: string[] | null;
}

export interface ImportSummary {
  recipients: number;
  items: number;
  graves: number;
  itemLinks: number;
  recipientLinks: number;
  /** Graves that needed a rounding correction. */
  correctedGraves: number;
}

/** The archive was rejected: nothing was imported, every problem is listed. */
export class ImportRejectedError extends Error {
  public constructor(public readonly problems: ImportProblem[]) {
    super("ImportInvalid");
    this.name = "ImportRejectedError";
  }
}

export function downloadImportTemplate(): Promise<Blob> {
  return getBlob("/odata/ImportTemplate");
}

export async function importArchive(file: File): Promise<ImportSummary> {
  const body = new FormData();
  body.append("file", file);
  try {
    const response = await request("/odata/ImportArchive", { method: "POST", body });
    return ((await response.json()) as { summary: ImportSummary }).summary;
  } catch (error) {
    const problems =
      error instanceof ApiError
        ? (error.body as { problems?: ImportProblem[] } | undefined)?.problems
        : undefined;
    if (Array.isArray(problems)) {
      throw new ImportRejectedError(problems);
    }
    throw error;
  }
}
