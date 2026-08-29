import ResourceBundle from "sap/base/i18n/ResourceBundle";
import MessageBox from "sap/m/MessageBox";
import type Controller from "sap/ui/core/mvc/Controller";
import ResourceModel from "sap/ui/model/resource/ResourceModel";

function toJson(response: Response): Promise<any> {
  return response.json().catch(() => ({}));
}

async function ensureOk(response: Response): Promise<void> {
  if (response.ok) {
    return;
  }

  const body = await toJson(response);
  throw new Error(body?.error?.code ?? `HTTP ${response.status}`);
}

function post(url: string, payload: unknown): Promise<Response> {
  return fetch(url, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
}

function patch(url: string, payload: unknown): Promise<Response> {
  return fetch(url, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
}

const CODE_TO_I18N: Record<string, string> = {
  StampWordRequired: "errorStampWordRequired",
  PriceRequired: "errorPriceRequired",
  RecipientAddressRequired: "errorRecipientAddressRequired",
  ItemNumberExists: "errorItemNumberExists",
  VatRateExists: "errorVatRateExists",
  VatRateNotConfigured: "errorVatRateNotConfigured",
  InvalidVatRate: "errorInvalidVatRate",
  GravesAlreadyInvoiced: "errorGravesAlreadyInvoiced",
  JobAlreadyRunning: "errorJobAlreadyRunning",
  JobTimeout: "errorJobTimeout",
  GraveNumberRequired: "errorGraveNumberRequired",
  InvalidDate: "errorInvalidDate",
  InvalidCount: "errorInvalidCount",
  ItemRequired: "errorItemRequired",
  GraveItemLinkExists: "errorGraveItemLinkExists",
  GraveRecipientLinkExists: "errorGraveRecipientLinkExists",
  GraveNotFound: "errorGraveNotFound",
  ItemNotFound: "errorItemNotFound",
  RecipientNotFound: "errorRecipientNotFound",
  RecipientNameRequired: "errorRecipientNameRequired",
  RecipientNumberExists: "errorRecipientNumberExists",
  RecipientNumberRequired: "errorRecipientNumberRequired",
  ItemNumberRequired: "errorItemNumberRequired",
  ItemDescriptionRequired: "errorItemDescriptionRequired",
  InvoiceNotFound: "errorInvoiceNotFound",
  NoLines: "errorNoLines",
  LanguageNotSupported: "errorLanguageNotSupported",
  ArchiveRequired: "errorArchiveRequired",
  GraveRecipientsImmutable: "errorGraveRecipientsImmutable",
  NotAGraveInvoice: "errorNotAGraveInvoice",
  GraveInvoiceGroupInconsistent: "errorGraveInvoiceGroupInconsistent",
  GraveInvoiceRevisionRequired: "errorGraveInvoiceRevisionRequired",
};

export async function showError(
  controller: Controller,
  error: unknown,
): Promise<void> {
  const raw = error instanceof Error ? error.message : String(error);
  const bundle = (
    controller.getOwnerComponent()!.getModel("i18n") as ResourceModel
  ).getResourceBundle() as ResourceBundle;

  // Known backend error codes are shown in a blocking MessageBox with a German
  // text; anything unexpected falls back to a generic MessageBox with the code.
  const key = CODE_TO_I18N[raw];
  if (key) {
    await MessageBox.error(bundle.getText(key) ?? key);
    return;
  }

  const generic = bundle.getText("errorGeneric") ?? "errorGeneric";
  await MessageBox.error(`${generic} (${raw})`);
}

export async function patchSettings(
  payload: Record<string, unknown>,
): Promise<void> {
  const response = await fetch("/odata/Settings", {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  await ensureOk(response);
}

export async function createVatRate(rate: number): Promise<void> {
  await ensureOk(await post("/odata/VatRates", { Rate: rate }));
}

export async function deleteVatRate(id: number): Promise<void> {
  await ensureOk(await fetch(`/odata/VatRates(${id})`, { method: "DELETE" }));
}

export async function createItem(item: {
  Number: string;
  Description: string;
  VatRate: number;
  DefaultPrice?: number | null;
}): Promise<void> {
  await ensureOk(
    await post("/odata/InvoiceItems", {
      ...item,
      DefaultPrice: item.DefaultPrice ?? null,
    }),
  );
}

export async function updateItem(
  id: number,
  fields: {
    Number?: string;
    Description?: string;
    VatRate?: number;
    DefaultPrice?: number | null;
  },
): Promise<void> {
  await ensureOk(await patch(`/odata/InvoiceItems(${id})`, fields));
}

export async function createRecipient(recipient: {
  Number?: string;
  Name: string;
  Address: string;
}): Promise<number> {
  const response = await post("/odata/Recipients", recipient);
  await ensureOk(response);
  const body = await response.json();
  // OData entity responses are PascalCase; accept camelCase defensively
  return (body.Id ?? body.id) as number;
}

export async function updateRecipient(
  id: number,
  fields: {
    Number?: string;
    Name?: string;
    Address?: string;
  },
): Promise<void> {
  await ensureOk(await patch(`/odata/Recipients(${id})`, fields));
}

export interface InvoiceLineDto {
  itemId: number;
  count: number;
  unitPrice: number | null;
}

export interface DocumentDto {
  Id: number;
  FileName: string;
  Kind: "Normal" | "Copy";
}

export async function createInvoice(
  recipientId: number,
  lines: InvoiceLineDto[],
): Promise<void> {
  await ensureOk(await post("/odata/CreateInvoice", { recipientId, lines }));
}

export async function createRevision(
  invoiceId: number,
  recipientId: number,
  lines: InvoiceLineDto[],
): Promise<void> {
  await ensureOk(
    await post("/odata/CreateRevision", { invoiceId, recipientId, lines }),
  );
}

export async function fetchDocuments(
  invoiceId: number,
): Promise<DocumentDto[]> {
  // only the id and file name are needed for the download links; keep the
  // PDF blobs out of the response
  const response = await fetch(
    `/odata/Invoices(${invoiceId})/Documents?$select=Id,FileName,Kind`,
  );
  await ensureOk(response);
  const body = await response.json();
  return body.value as DocumentDto[];
}

/** The document's URL; the backend serves it for display in the browser. */
export function documentUrl(documentId: number): string {
  return `/odata/Documents(${documentId})/$value`;
}

export async function bulkDownload(
  kind: "Normal" | "Copy",
  fileName: string,
  year?: number,
  half?: number,
): Promise<void> {
  const payload: Record<string, unknown> = { kind };
  if (year != null && half != null) {
    payload.year = year;
    payload.half = half;
  }

  const response = await post("/odata/BulkDownloadDocuments", payload);
  await ensureOk(response);
  const blob = await response.blob();
  await downloadBlob(blob, fileName);
}

export interface YearHalfOption {
  key: string;
  text: string;
}

// Half-year keys ("2026-1", "2026-2") for the given year range, newest first.
export function halfYearOptions(
  yearsBack: number,
  yearsAhead: number,
): YearHalfOption[] {
  const currentYear = new Date().getFullYear();
  const options: YearHalfOption[] = [];
  for (
    let year = currentYear + yearsAhead;
    year >= currentYear - yearsBack;
    year--
  ) {
    options.push({ key: `${year}-2`, text: `H2 ${year}` });
    options.push({ key: `${year}-1`, text: `H1 ${year}` });
  }
  return options;
}

// Year-only keys ("2026") for the given year range, newest first.
export function yearOptions(
  yearsBack: number,
  yearsAhead: number,
): YearHalfOption[] {
  const currentYear = new Date().getFullYear();
  const options: YearHalfOption[] = [];
  for (
    let year = currentYear + yearsAhead;
    year >= currentYear - yearsBack;
    year--
  ) {
    options.push({ key: String(year), text: String(year) });
  }
  return options;
}

export async function downloadBlob(
  blob: Blob,
  fileName: string,
): Promise<void> {
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export interface JobDto {
  Id: number;
  Status: string;
  ProgressPercent: number;
  ResultJson?: string;
}

export async function generateGraveInvoices(
  year: number,
  half: number,
): Promise<JobDto> {
  const response = await post("/odata/GenerateGraveInvoices", { year, half });
  await ensureOk(response);
  return (await response.json()) as JobDto;
}

export interface GraveStatusRow {
  Id: number;
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
  Invoiced: boolean;
}

export async function fetchGraveStatus(
  year: number,
  half: number,
): Promise<GraveStatusRow[]> {
  const response = await fetch(
    `/odata/GraveInvoicingStatus?year=${year}&half=${half}`,
  );
  await ensureOk(response);
  const body = await response.json();
  return body.value as GraveStatusRow[];
}

export interface GraveSearchResult {
  Id: number;
  GraveNumber: string;
  RecipientNames: string[];
}

export async function searchGraves(term: string): Promise<GraveSearchResult[]> {
  const response = await fetch(
    `/odata/SearchGraves?term=${encodeURIComponent(term)}`,
  );
  await ensureOk(response);
  const body = await response.json();
  return body.value as GraveSearchResult[];
}

export interface GraveItemInput {
  itemId: number;
  count: number;
  unitPrice: number | null;
}

export interface SaveGraveInput {
  graveId: number | null;
  graveNumber: string;
  startDate: string;
  endDate: string;
  items: GraveItemInput[];
  recipientIds: number[];
}

// The grave dialog keeps everything in a client-side draft, so the grave and
// its complete set of item and recipient links are saved in one request.
export async function saveGrave(input: SaveGraveInput): Promise<number> {
  const response = await post("/odata/SaveGrave", input);
  await ensureOk(response);
  const body = await response.json();
  // OData entity responses are PascalCase; accept camelCase defensively
  return (body.Id ?? body.id) as number;
}

export async function fetchGraveDetails(graveId: number): Promise<any> {
  const response = await fetch(
    `/odata/Graves(${graveId})?$expand=Items($expand=Item),Recipients($expand=Recipient)`,
  );
  await ensureOk(response);
  return response.json();
}

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

/** Thrown when the archive was rejected: nothing was imported and every
 *  problem is listed with the file and row it belongs to. */
export class ImportRejectedError extends Error {
  public constructor(public readonly problems: ImportProblem[]) {
    super("ImportInvalid");
  }
}

export async function downloadImportTemplate(fileName: string): Promise<void> {
  const response = await fetch("/odata/ImportTemplate");
  await ensureOk(response);
  await downloadBlob(await response.blob(), fileName);
}

export async function importArchive(file: File): Promise<ImportSummary> {
  const body = new FormData();
  body.append("file", file);
  const response = await fetch("/odata/ImportArchive", { method: "POST", body });
  if (response.ok) {
    return (await response.json()).summary as ImportSummary;
  }

  const payload = await toJson(response);
  if (Array.isArray(payload?.problems)) {
    throw new ImportRejectedError(payload.problems as ImportProblem[]);
  }

  throw new Error(payload?.error?.code ?? `HTTP ${response.status}`);
}

export interface InvoiceLineAmounts {
  Count: number;
  UnitPrice: number;
  VatRate: number;
}

export interface InvoiceTotals {
  net: number;
  vat: number;
  gross: number;
}

/** A line's net total, rounded to the cent the way the document rounds it. */
export function lineTotal(line: InvoiceLineAmounts): number {
  return Math.round(line.Count * line.UnitPrice * 100) / 100;
}

/** Net, VAT and gross of an invoice. Each line is rounded on its own, exactly
 *  as DocumentDataBuilder does it, so a detail page never disagrees with the
 *  PDF it links to. */
export function invoiceTotals(lines: InvoiceLineAmounts[]): InvoiceTotals {
  let netCents = 0;
  let vatCents = 0;
  for (const line of lines) {
    const lineCents = Math.round(line.Count * line.UnitPrice * 100);
    netCents += lineCents;
    vatCents += Math.round((lineCents * line.VatRate) / 100);
  }
  return {
    net: netCents / 100,
    vat: vatCents / 100,
    gross: (netCents + vatCents) / 100,
  };
}

/** "H1 2026" — the label the half-year selects already use. */
export function halfYearText(year: number, half: number): string {
  return `H${half} ${year}`;
}

export interface GraveInvoiceGroupLine {
  ItemId: number;
  ItemNumber: string;
  Description: string;
  Count: number;
  UnitPrice: number;
  VatRate: number;
}

export interface GraveInvoiceGroup {
  GraveId: number;
  GraveNumber: string;
  PeriodYear: number;
  PeriodHalf: number;
  Recipients: { Id: number; Name: string; InvoiceId: number }[];
  Lines: GraveInvoiceGroupLine[];
}

/** The grave, half-year and recipients a grave invoice belongs to, with the
 *  grave's lines as they were invoiced — not as the grave looks today. */
export async function fetchGraveInvoiceGroup(
  invoiceId: number,
): Promise<GraveInvoiceGroup> {
  const response = await fetch(`/odata/GraveInvoiceGroup?invoiceId=${invoiceId}`);
  await ensureOk(response);
  return (await response.json()) as GraveInvoiceGroup;
}

/** Revises every recipient invoice of the grave and half-year the given invoice
 *  belongs to. `lines` are grave-level: the full count at the full price. */
export async function createGraveInvoiceRevision(
  invoiceId: number,
  lines: InvoiceLineDto[],
): Promise<void> {
  await ensureOk(
    await post("/odata/CreateGraveInvoiceRevision", { invoiceId, lines }),
  );
}
