/**
 * Everything the EDM does not describe (D-001), and the five actions whose
 * parameters $metadata leaves out. Payloads and response shapes mirror
 * frontend/src/model/api.ts, which the backend was built against.
 */
import { type InvoiceLine, type Job, JobStatus } from "./generated";
import { ApiError, getBlob, getJson, postBlob, postJson, request } from "./http";
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

// ---- recipients and items -------------------------------------------------

/** POST through fetch rather than the generated client: an empty number is
 *  left out so the backend assigns one, which the generated create type (Number
 *  required) cannot express. */
export async function createRecipient(input: {
  Number?: string;
  Name: string;
  Address: string;
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
  Graves: { Id: number; GraveNumber: string }[];
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

export interface GraveStatusRow {
  Id: number;
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
  Invoiced: boolean;
  InvoiceCount: number;
  PaidCount: number;
}

export async function fetchGraveStatus(year: number, half: number): Promise<GraveStatusRow[]> {
  const url = `/odata/GraveInvoicingStatus?year=${year}&half=${half}`;
  return (await getJson<{ value: GraveStatusRow[] }>(url)).value;
}

export interface GraveSearchResult {
  Id: number;
  GraveNumber: string;
  RecipientNames: string[];
}

export async function searchGraves(term: string): Promise<GraveSearchResult[]> {
  const url = `/odata/SearchGraves?term=${encodeURIComponent(term)}`;
  return (await getJson<{ value: GraveSearchResult[] }>(url)).value;
}

export interface SaveGraveInput {
  graveId: number | null;
  graveNumber: string;
  startDate: string;
  endDate: string;
  items: InvoiceLineInput[];
  recipientIds: number[];
}

/** The grave and its complete item and recipient links, in one request. */
export async function saveGrave(input: SaveGraveInput): Promise<number> {
  const body = await postJson<{ Id?: number; id?: number } | undefined>("/odata/SaveGrave", input);
  return Number(body?.Id ?? body?.id);
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
  };
}

export interface GraveDetails {
  Id: number;
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
  CreatedAt: string;
  Items: GraveItemLink[];
  Recipients: { Recipient: { Id: number; Name: string; Address: string } }[];
}

/** Graves(id) answers hand-built JSON with the links already expanded. */
export function fetchGraveDetails(graveId: number): Promise<GraveDetails> {
  return getJson<GraveDetails>(
    `/odata/Graves(${graveId})?$expand=Items($expand=Item),Recipients($expand=Recipient)`,
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

/** Polls the job once a second until it succeeds or fails; throws
 *  ApiError("JobTimeout") after ten minutes. */
export async function pollJob(
  jobId: number,
  onProgress: (percent: number) => void,
  timeoutMs = JOB_TIMEOUT_MS,
): Promise<Job> {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    if (Date.now() > deadline) {
      throw new ApiError("JobTimeout");
    }
    const job = (await odata.Jobs(jobId).query().execute()).data;
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
  message: string;
}

export interface ImportSummary {
  recipients: number;
  items: number;
  graves: number;
  itemLinks: number;
  recipientLinks: number;
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
