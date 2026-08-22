import type Controller from "sap/ui/core/mvc/Controller";
import MessageToast from "sap/m/MessageToast";
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
};

export async function showError(controller: Controller, error: unknown): Promise<void> {
    const raw = error instanceof Error ? error.message : String(error);
    const bundle = (controller.getOwnerComponent()!.getModel("i18n") as ResourceModel)
        .getResourceBundle() as ResourceBundle;

    // Known backend error codes are shown as a short toast with a German text;
    // anything unexpected falls back to a generic MessageBox.
    const key = CODE_TO_I18N[raw];
    if (key) {
        await MessageToast.show(bundle.getText(key) ?? key);
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

export async function createRecipient(recipient: { Name: string; Address: string }): Promise<void> {
    await ensureOk(await post("/odata/Recipients", recipient));
}
