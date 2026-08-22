import type Controller from "sap/ui/core/mvc/Controller";
import MessageBox from "sap/m/MessageBox";
import ResourceBundle from "sap/base/i18n/ResourceBundle";
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
    GraveNotSaved: "errorGraveNotSaved",
};

export async function showError(controller: Controller, error: unknown): Promise<void> {
    const raw = error instanceof Error ? error.message : String(error);
    const bundle = (controller.getOwnerComponent()!.getModel("i18n") as ResourceModel)
        .getResourceBundle() as ResourceBundle;

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
    Number: string; Description: string; VatRate: number; DefaultPrice?: number | null;
}): Promise<void> {
    await ensureOk(await post("/odata/InvoiceItems", { ...item, DefaultPrice: item.DefaultPrice ?? null }));
}

export async function updateItem(id: number, fields: {
    Number?: string; Description?: string; VatRate?: number; DefaultPrice?: number | null;
}): Promise<void> {
    await ensureOk(await patch(`/odata/InvoiceItems(${id})`, fields));
}

export async function createRecipient(recipient: { Name: string; Address: string }): Promise<number> {
    const response = await post("/odata/Recipients", recipient);
    await ensureOk(response);
    const body = await response.json();
    // OData entity responses are PascalCase; accept camelCase defensively
    return (body.Id ?? body.id) as number;
}

export async function updateRecipient(id: number, fields: {
    Name?: string; Address?: string;
}): Promise<void> {
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
}

export async function createInvoice(recipientId: number, lines: InvoiceLineDto[]): Promise<void> {
    await ensureOk(await post("/odata/CreateInvoice", { recipientId, lines }));
}

export async function createRevision(invoiceId: number, recipientId: number, lines: InvoiceLineDto[]): Promise<void> {
    await ensureOk(await post("/odata/CreateRevision", { invoiceId, recipientId, lines }));
}

export async function fetchDocuments(invoiceId: number): Promise<DocumentDto[]> {
    // only the id and file name are needed for the download links; keep the
    // PDF blobs out of the response
    const response = await fetch(`/odata/Invoices(${invoiceId})/Documents?$select=Id,FileName`);
    await ensureOk(response);
    const body = await response.json();
    return body.value as DocumentDto[];
}

export function downloadUrl(documentId: number): string {
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

export async function downloadBlob(blob: Blob, fileName: string): Promise<void> {
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

export async function generateGraveInvoices(year: number, half: number): Promise<JobDto> {
    const response = await post("/odata/GenerateGraveInvoices", { year, half });
    await ensureOk(response);
    return (await response.json()) as JobDto;
}

export async function fetchGraveStatus(year: number, half: number): Promise<any[]> {
    const response = await fetch(`/odata/GraveInvoicingStatus?year=${year}&half=${half}`);
    await ensureOk(response);
    const body = await response.json();
    return body.value as any[];
}

export interface GraveSearchResult {
    id: number;
    graveNumber: string;
    recipientNames: string[];
}

export async function searchGraves(term: string): Promise<GraveSearchResult[]> {
    const response = await fetch(`/odata/SearchGraves?term=${encodeURIComponent(term)}`);
    await ensureOk(response);
    const body = await response.json();
    return body.value as GraveSearchResult[];
}

export async function createGrave(grave: {
    GraveNumber: string; StartDate: string; EndDate: string;
}): Promise<number> {
    const response = await post("/odata/Graves", grave);
    await ensureOk(response);
    const body = await response.json();
    return body.Id as number;
}

export async function patchGrave(graveId: number, fields: Record<string, unknown>): Promise<void> {
    const response = await fetch(`/odata/Graves(${graveId})`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(fields),
    });
    await ensureOk(response);
}

export async function createGraveItemLink(graveId: number, itemId: number, count: number): Promise<void> {
    await ensureOk(await post("/odata/GraveItems", { GraveId: graveId, ItemId: itemId, Count: count }));
}

export async function createGraveRecipientLink(graveId: number, recipientId: number): Promise<void> {
    await ensureOk(await post("/odata/GraveRecipients", { GraveId: graveId, RecipientId: recipientId }));
}

export async function fetchGraveDetails(graveId: number): Promise<any> {
    const response = await fetch(
        `/odata/Graves(${graveId})?$expand=Items($expand=Item),Recipients($expand=Recipient)`);
    await ensureOk(response);
    return response.json();
}

export async function deleteGraveItemLink(linkId: number): Promise<void> {
    await ensureOk(await fetch(`/odata/GraveItems(${linkId})`, { method: "DELETE" }));
}

export async function deleteGraveRecipientLink(linkId: number): Promise<void> {
    await ensureOk(await fetch(`/odata/GraveRecipients(${linkId})`, { method: "DELETE" }));
}
