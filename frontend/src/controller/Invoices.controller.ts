import BaseController from "./Base.controller";
import * as api from "../model/api";
import ODataModel from "sap/ui/model/odata/v4/ODataModel";
import ODataListBinding from "sap/ui/model/odata/v4/ODataListBinding";
import Filter from "sap/ui/model/Filter";
import FilterOperator from "sap/ui/model/FilterOperator";
import Context from "sap/ui/model/odata/v4/Context";
import Select from "sap/m/Select";
import JSONModel from "sap/ui/model/json/JSONModel";

export default class Invoices extends BaseController {
    public onInit(): void {
        // routed views run onInit before they are added to the App, so the
        // odata model is not visible via view.getModel() yet
        const view = this.getView()!;
        const model = this.getOwnerComponent()!.getModel("odata") as ODataModel;
        view.setBindingContext(model.createBindingContext("/") as Context, "odata");
    }

    public onHalfYearChange(event: any): void {
        const key = (event.getSource() as Select).getSelectedKey();
        const binding = (this.byId("invoicesTable") as any).getBinding("items") as ODataListBinding;
        if (!key) {
            binding.filter([]);
            return;
        }

        const [year, half] = key.split("-").map(Number);
        binding.filter(new Filter({
            filters: [
                new Filter("PeriodYear", FilterOperator.EQ, year),
                new Filter("PeriodHalf", FilterOperator.EQ, half),
            ],
            and: true,
        }));
    }

    public async onCreateInvoice(): Promise<void> {
        await this.openInvoiceDialog(null);
    }

    public async onEditInvoice(event: any): Promise<void> {
        const context = event.getSource().getBindingContext("odata") as Context;
        await this.openInvoiceDialog(context);
    }

    private async openInvoiceDialog(invoiceContext: Context | null): Promise<void> {
        if (!this._dialog) {
            // loadFragment passes this controller and the view ID prefix so that
            // this.byId(...) finds the dialog controls and IDs stay unique
            this._dialog = await this.loadFragment({
                name: "burkard.invoicing.view.fragments.InvoiceDialog",
            }) as any;
        }

        const lines = invoiceContext
            ? await this.loadLines(invoiceContext.getProperty("Id") as number)
            : [{ itemId: null, count: 1, unitPrice: null }];
        this._dialog.setModel(new JSONModel({
            recipientId: invoiceContext?.getProperty("RecipientId") ?? null,
            invoiceId: invoiceContext?.getProperty("Id") ?? null,
            lines,
        }), "invoiceDraft");
        this._dialog.open();
    }

    private async loadLines(invoiceId: number): Promise<any[]> {
        const response = await fetch(`/odata/Invoices(${invoiceId})/Lines`);
        if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
        }
        const body = await response.json();
        return (body.value as any[]).map((line) => ({
            itemId: line.ItemId,
            count: line.Count,
            unitPrice: line.UnitPrice,
        }));
    }

    public onAddLine(): void {
        const model = this._dialog.getModel("invoiceDraft") as JSONModel;
        const lines = (model.getProperty("/lines") as any[]) ?? [];
        lines.push({ itemId: null, count: 1, unitPrice: null });
        model.setProperty("/lines", lines);
    }

    public async onInvoiceDialogSave(): Promise<void> {
        const model = this._dialog.getModel("invoiceDraft") as JSONModel;
        const draft = model.getData() as any;
        const lines = (draft.lines as any[]).map((line) => ({
            itemId: Number(line.itemId),
            count: Number(line.count),
            unitPrice: line.unitPrice ? Number(line.unitPrice) : null,
        }));

        try {
            if (draft.invoiceId) {
                await api.createRevision(Number(draft.invoiceId), Number(draft.recipientId), lines);
            } else {
                await api.createInvoice(Number(draft.recipientId), lines);
            }

            this._dialog.close();
            ((this.byId("invoicesTable") as any).getBinding("items") as ODataListBinding).refresh();
        } catch (error) {
            await api.showError(this, error);
        }
    }

    public onInvoiceDialogCancel(): void {
        this._dialog.close();
    }

    public async onDownloadNormal(event: any): Promise<void> {
        await this.download(event.getSource().getBindingContext("odata") as Context, false);
    }

    public async onDownloadCopy(event: any): Promise<void> {
        await this.download(event.getSource().getBindingContext("odata") as Context, true);
    }

    private async download(context: Context, isCopy: boolean): Promise<void> {
        const invoiceId = context.getProperty("Id") as number;
        const documents = await api.fetchDocuments(invoiceId);
        const match = documents.find((d) => d.FileName.includes("Kopie") === isCopy);
        if (match) {
            window.open(api.downloadUrl(match.Id), "_blank");
        }
    }

    public async onBulkNormal(): Promise<void> {
        await api.bulkDownload("Normal", "rechnungen.zip");
    }

    public async onBulkCopy(): Promise<void> {
        await api.bulkDownload("Copy", "kopien.zip");
    }

    public formatDate(value: Date | string | null | undefined): string {
        if (value == null) {
            return "";
        }
        const date = value instanceof Date ? value : new Date(value);
        return date.toLocaleDateString("de-DE");
    }

    private _dialog: any;
}
