import ResourceBundle from "sap/base/i18n/ResourceBundle";
import MessageBox from "sap/m/MessageBox";
import type Controller from "sap/ui/core/mvc/Controller";
import ResourceModel from "sap/ui/model/resource/ResourceModel";

/** What the backend puts in a failed response; absent when it sent no JSON. */
interface ErrorBody {
  error?: { code?: string };
}

/** An OData collection response: the rows arrive under `value`. */
interface ODataCollection<T> {
  value: T[];
}

/** An entity response. PascalCase from the EDM serialiser, camelCase from the
 *  plain MVC routes under `/odata` (D-001), so accept either. */
interface CreatedEntity {
  Id?: number;
  id?: number;
}

/** Reads a response body, answering `{}` rather than throwing on a non-JSON one. */
function toJson<T>(response: Response): Promise<T> {
  return response.json().catch(() => ({})) as Promise<T>;
}

async function ensureOk(response: Response): Promise<void> {
  if (response.ok) {
    return;
  }

  const body = await toJson<ErrorBody>(response);
  throw new Error(body.error?.code ?? `HTTP ${response.status}`);
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

/**
 * Opens the message box for a failed call. `MessageBox.error` returns as soon
 * as the dialog is on screen, so there is nothing here to await.
 */
export function showError(controller: Controller, error: unknown): void {
  const raw = error instanceof Error ? error.message : String(error);
  const bundle = (
    controller.getOwnerComponent()!.getModel("i18n") as ResourceModel
  ).getResourceBundle() as ResourceBundle;

  // Known backend error codes are shown in a blocking MessageBox with a German
  // text; anything unexpected falls back to a generic MessageBox with the code.
  const key = CODE_TO_I18N[raw];
  if (key) {
    MessageBox.error(bundle.getText(key) ?? key);
    return;
  }

  const generic = bundle.getText("errorGeneric") ?? "errorGeneric";
  MessageBox.error(`${generic} (${raw})`);
}

export async function patchSettings(payload: Record<string, unknown>): Promise<void> {
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
  PriceIncludesVat?: boolean;
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
    PriceIncludesVat?: boolean;
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
  const body = await toJson<CreatedEntity>(response);
  // OData entity responses are PascalCase; accept camelCase defensively
  return (body.Id ?? body.id)!;
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

export async function createInvoice(recipientId: number, lines: InvoiceLineDto[]): Promise<void> {
  await ensureOk(await post("/odata/CreateInvoice", { recipientId, lines }));
}

export async function createRevision(
  invoiceId: number,
  recipientId: number,
  lines: InvoiceLineDto[],
): Promise<void> {
  await ensureOk(await post("/odata/CreateRevision", { invoiceId, recipientId, lines }));
}

export async function fetchDocuments(invoiceId: number): Promise<DocumentDto[]> {
  // only the id and file name are needed for the download links; keep the
  // PDF blobs out of the response
  const response = await fetch(`/odata/Invoices(${invoiceId})/Documents?$select=Id,FileName,Kind`);
  await ensureOk(response);
  const body = await toJson<ODataCollection<DocumentDto>>(response);
  return body.value;
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
  downloadBlob(blob, fileName);
}

export interface YearHalfOption {
  key: string;
  text: string;
}

/** The half-year today falls in: Jan-Jun is 1, Jul-Dec is 2. The graves
 *  overview, the grave detail page and the generation dialog all open on it, so
 *  they agree on which graves are still to be invoiced. */
export function currentHalfYear(): [number, number] {
  const now = new Date();
  return [now.getFullYear(), now.getMonth() < 6 ? 1 : 2];
}

// Half-year keys ("2026-1", "2026-2") for the given year range, newest first.
export function halfYearOptions(yearsBack: number, yearsAhead: number): YearHalfOption[] {
  const currentYear = new Date().getFullYear();
  const options: YearHalfOption[] = [];
  for (let year = currentYear + yearsAhead; year >= currentYear - yearsBack; year--) {
    options.push({ key: `${year}-2`, text: `H2 ${year}` });
    options.push({ key: `${year}-1`, text: `H1 ${year}` });
  }
  return options;
}

// Year-only keys ("2026") for the given year range, newest first.
export function yearOptions(yearsBack: number, yearsAhead: number): YearHalfOption[] {
  const currentYear = new Date().getFullYear();
  const options: YearHalfOption[] = [];
  for (let year = currentYear + yearsAhead; year >= currentYear - yearsBack; year--) {
    options.push({ key: String(year), text: String(year) });
  }
  return options;
}

export function downloadBlob(blob: Blob, fileName: string): void {
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

/** A grave the run did not invoice, named and — since the reason column — explained. */
export type SkippedGrave = string | { Number: string; Reason?: string };

/** {@link JobDto.ResultJson}, parsed. */
export interface GenerationResult {
  InvoicedGraves?: number;
  SkippedGraves?: SkippedGrave[];
}

export async function generateGraveInvoices(year: number, half: number): Promise<JobDto> {
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

export async function fetchGraveStatus(year: number, half: number): Promise<GraveStatusRow[]> {
  const response = await fetch(`/odata/GraveInvoicingStatus?year=${year}&half=${half}`);
  await ensureOk(response);
  const body = await toJson<ODataCollection<GraveStatusRow>>(response);
  return body.value;
}

export interface GraveSearchResult {
  Id: number;
  GraveNumber: string;
  RecipientNames: string[];
}

export async function searchGraves(term: string): Promise<GraveSearchResult[]> {
  const response = await fetch(`/odata/SearchGraves?term=${encodeURIComponent(term)}`);
  await ensureOk(response);
  const body = await toJson<ODataCollection<GraveSearchResult>>(response);
  return body.value;
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
  const body = await toJson<CreatedEntity>(response);
  // OData entity responses are PascalCase; accept camelCase defensively
  return (body.Id ?? body.id)!;
}

/** An InvoiceItems row, as the grave expand returns it. */
export interface ItemRow {
  Id: number;
  Number: string;
  Description: string;
  VatRate: number;
  DefaultPrice: number | null;
  PriceIncludesVat: boolean;
}

export interface GraveItemLink {
  Count: number;
  /** The link's own price. Without one the item's default is billed. */
  UnitPrice: number | null;
  Item: ItemRow;
}

export interface GraveRecipientLink {
  Recipient: { Id: number; Name: string; Address: string };
}

/** A Graves row with its item and recipient links expanded. */
export interface GraveDetails {
  Id: number;
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
  CreatedAt: string;
  Items: GraveItemLink[];
  Recipients: GraveRecipientLink[];
}

export async function fetchGraveDetails(graveId: number): Promise<GraveDetails> {
  const response = await fetch(
    `/odata/Graves(${graveId})?$expand=Items($expand=Item),Recipients($expand=Recipient)`,
  );
  await ensureOk(response);
  return toJson<GraveDetails>(response);
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
  downloadBlob(await response.blob(), fileName);
}

export async function importArchive(file: File): Promise<ImportSummary> {
  const body = new FormData();
  body.append("file", file);
  const response = await fetch("/odata/ImportArchive", { method: "POST", body });
  if (response.ok) {
    const body = await toJson<{ summary: ImportSummary }>(response);
    return body.summary;
  }

  const payload = await toJson<ErrorBody & { problems?: ImportProblem[] }>(response);
  if (Array.isArray(payload.problems)) {
    throw new ImportRejectedError(payload.problems);
  }

  throw new Error(payload.error?.code ?? `HTTP ${response.status}`);
}

/**
 * An Invoices row as the OData V4 model hands it over. Edm.Decimal is
 * serialised as a string, so every amount is converted where it is read.
 */
export interface InvoiceRow {
  Id: number;
  RootId: number;
  InvoiceNumber: string;
  RevisionNumber: number;
  IsLatestRevision: boolean;
  RecipientId: number;
  RecipientName: string;
  RecipientAddress: string;
  GeneratedAt: string;
  GraveId: number | null;
  PeriodYear: number | null;
  PeriodHalf: number | null;
  SharePercent: number | string | null;
  Lines?: InvoiceLineRow[];
}

export interface InvoiceLineRow {
  LineNumber: number;
  ItemId: number | null;
  ItemNumber: string;
  Description: string;
  Count: number | string;
  UnitPrice: number | string;
  GraveUnitPrice: number | string | null;
  VatRate: number | string;
}

/** A GraveInvoiceRuns row: which half-year a grave has already been invoiced for. */
export interface GraveInvoiceRunRow {
  Id: number;
  GraveId: number;
  Year: number;
  Half: number;
  CreatedAt: string;
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
  Recipients: GraveInvoiceRecipient[];
  Lines: GraveInvoiceGroupLine[];
}

/** One recipient billed for a grave's half-year, and the invoice they got. */
export interface GraveInvoiceRecipient {
  Id: number;
  Name: string;
  InvoiceId: number;
  RootId: number;
}

/** The grave, half-year and recipients a grave invoice belongs to, with the
 *  grave's lines as they were invoiced — not as the grave looks today. */
export async function fetchGraveInvoiceGroup(invoiceId: number): Promise<GraveInvoiceGroup> {
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
  await ensureOk(await post("/odata/CreateGraveInvoiceRevision", { invoiceId, lines }));
}
