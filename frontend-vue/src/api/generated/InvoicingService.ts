import type { ODataModelResponseV4, ODataCollectionResponseV4 } from "@odata2ts/odata-core";
import { ODataHttpMethods } from "@odata2ts/http-client-api";
import { ODataService, UrlRequestCmd } from "@odata2ts/odata-service";
import type { VatRateId } from "./burkard-invoicing-domain-entities/vat-rate/VatRate.js";
import type { InvoiceItemId } from "./burkard-invoicing-domain-entities/invoice-item/InvoiceItem.js";
import type { RecipientId } from "./burkard-invoicing-domain-entities/recipient/Recipient.js";
import type { GraveId } from "./burkard-invoicing-domain-entities/grave/Grave.js";
import type { GraveItemId } from "./burkard-invoicing-domain-entities/grave-item/GraveItem.js";
import type { GraveRecipientId } from "./burkard-invoicing-domain-entities/grave-recipient/GraveRecipient.js";
import type { GraveInvoiceRunId } from "./burkard-invoicing-domain-entities/grave-invoice-run/GraveInvoiceRun.js";
import type { InvoiceId, Invoice } from "./burkard-invoicing-domain-entities/invoice/Invoice.js";
import type { DocumentId } from "./burkard-invoicing-domain-entities/document/Document.js";
import type { JobId, Job } from "./burkard-invoicing-domain-entities/job/Job.js";
import { VatRateService, VatRateCollectionService } from "./burkard-invoicing-domain-entities/vat-rate/VatRateService.js";
import { InvoiceItemService, InvoiceItemCollectionService } from "./burkard-invoicing-domain-entities/invoice-item/InvoiceItemService.js";
import { RecipientService, RecipientCollectionService } from "./burkard-invoicing-domain-entities/recipient/RecipientService.js";
import { GraveService, GraveCollectionService } from "./burkard-invoicing-domain-entities/grave/GraveService.js";
import { GraveItemService, GraveItemCollectionService } from "./burkard-invoicing-domain-entities/grave-item/GraveItemService.js";
import { GraveRecipientService, GraveRecipientCollectionService } from "./burkard-invoicing-domain-entities/grave-recipient/GraveRecipientService.js";
import { GraveInvoiceRunService, GraveInvoiceRunCollectionService } from "./burkard-invoicing-domain-entities/grave-invoice-run/GraveInvoiceRunService.js";
import { InvoiceService, InvoiceCollectionService } from "./burkard-invoicing-domain-entities/invoice/InvoiceService.js";
import { DocumentService, DocumentCollectionService } from "./burkard-invoicing-domain-entities/document/DocumentService.js";
import { JobService, JobCollectionService } from "./burkard-invoicing-domain-entities/job/JobService.js";
import { GlobalSettingsService } from "./burkard-invoicing-domain-entities/global-settings/GlobalSettingsService.js";
import { QCreateInvoice, QCreateRevision, QCreateGraveInvoiceRevision, QGenerateGraveInvoices, QSetInvoicePaid } from "./QInvoicing.js";

export class InvoicingService extends ODataService {
    private _Settings?: GlobalSettingsService;
    private _QCreateInvoice?: QCreateInvoice;
    private _QCreateRevision?: QCreateRevision;
    private _QCreateGraveInvoiceRevision?: QCreateGraveInvoiceRevision;
    private _QGenerateGraveInvoices?: QGenerateGraveInvoices;
    private _QSetInvoicePaid?: QSetInvoicePaid;

    public VatRates(): VatRateCollectionService;
    public VatRates(id: VatRateId): VatRateService;
    public VatRates(id?: VatRateId | undefined) {
        const fieldName = "VatRates";
        const { client, path, options } = this.__base;
        const collection = new VatRateCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public InvoiceItems(): InvoiceItemCollectionService;
    public InvoiceItems(id: InvoiceItemId): InvoiceItemService;
    public InvoiceItems(id?: InvoiceItemId | undefined) {
        const fieldName = "InvoiceItems";
        const { client, path, options } = this.__base;
        const collection = new InvoiceItemCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public Recipients(): RecipientCollectionService;
    public Recipients(id: RecipientId): RecipientService;
    public Recipients(id?: RecipientId | undefined) {
        const fieldName = "Recipients";
        const { client, path, options } = this.__base;
        const collection = new RecipientCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public Graves(): GraveCollectionService;
    public Graves(id: GraveId): GraveService;
    public Graves(id?: GraveId | undefined) {
        const fieldName = "Graves";
        const { client, path, options } = this.__base;
        const collection = new GraveCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public GraveItems(): GraveItemCollectionService;
    public GraveItems(id: GraveItemId): GraveItemService;
    public GraveItems(id?: GraveItemId | undefined) {
        const fieldName = "GraveItems";
        const { client, path, options } = this.__base;
        const collection = new GraveItemCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public GraveRecipients(): GraveRecipientCollectionService;
    public GraveRecipients(id: GraveRecipientId): GraveRecipientService;
    public GraveRecipients(id?: GraveRecipientId | undefined) {
        const fieldName = "GraveRecipients";
        const { client, path, options } = this.__base;
        const collection = new GraveRecipientCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public GraveInvoiceRuns(): GraveInvoiceRunCollectionService;
    public GraveInvoiceRuns(id: GraveInvoiceRunId): GraveInvoiceRunService;
    public GraveInvoiceRuns(id?: GraveInvoiceRunId | undefined) {
        const fieldName = "GraveInvoiceRuns";
        const { client, path, options } = this.__base;
        const collection = new GraveInvoiceRunCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public Invoices(): InvoiceCollectionService;
    public Invoices(id: InvoiceId): InvoiceService;
    public Invoices(id?: InvoiceId | undefined) {
        const fieldName = "Invoices";
        const { client, path, options } = this.__base;
        const collection = new InvoiceCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public Documents(): DocumentCollectionService;
    public Documents(id: DocumentId): DocumentService;
    public Documents(id?: DocumentId | undefined) {
        const fieldName = "Documents";
        const { client, path, options } = this.__base;
        const collection = new DocumentCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public Jobs(): JobCollectionService;
    public Jobs(id: JobId): JobService;
    public Jobs(id?: JobId | undefined) {
        const fieldName = "Jobs";
        const { client, path, options } = this.__base;
        const collection = new JobCollectionService(client, path, fieldName, { ...options, subtype: false });
        return typeof id === "undefined" || id === null ? collection : collection.byId(id);
    }

    public Settings() {
        if(!this._Settings) {
          const { client, path, options } = this.__base;
          this._Settings = new GlobalSettingsService(client, path, "Settings", options)
        }

        return this._Settings
    }

    public CreateInvoice() {
        if(!this._QCreateInvoice) {
          this._QCreateInvoice = new QCreateInvoice()
        }

        const { addFullPath, client, getDefaultHeaders } = this.__base;
        const url = addFullPath(this._QCreateInvoice.buildUrl());
        
        return new UrlRequestCmd<ODataModelResponseV4<Invoice>>(client,ODataHttpMethods.Post,url, undefined,{ headers: getDefaultHeaders(), mainResponseConverter: this._QCreateInvoice.getResponseConverter()});
    }

    public CreateRevision() {
        if(!this._QCreateRevision) {
          this._QCreateRevision = new QCreateRevision()
        }

        const { addFullPath, client, getDefaultHeaders } = this.__base;
        const url = addFullPath(this._QCreateRevision.buildUrl());
        
        return new UrlRequestCmd<ODataModelResponseV4<Invoice>>(client,ODataHttpMethods.Post,url, undefined,{ headers: getDefaultHeaders(), mainResponseConverter: this._QCreateRevision.getResponseConverter()});
    }

    public CreateGraveInvoiceRevision() {
        if(!this._QCreateGraveInvoiceRevision) {
          this._QCreateGraveInvoiceRevision = new QCreateGraveInvoiceRevision()
        }

        const { addFullPath, client, getDefaultHeaders } = this.__base;
        const url = addFullPath(this._QCreateGraveInvoiceRevision.buildUrl());
        
        return new UrlRequestCmd<ODataCollectionResponseV4<Invoice>>(client,ODataHttpMethods.Post,url, undefined,{ headers: getDefaultHeaders(), mainResponseConverter: this._QCreateGraveInvoiceRevision.getResponseConverter()});
    }

    public GenerateGraveInvoices() {
        if(!this._QGenerateGraveInvoices) {
          this._QGenerateGraveInvoices = new QGenerateGraveInvoices()
        }

        const { addFullPath, client, getDefaultHeaders } = this.__base;
        const url = addFullPath(this._QGenerateGraveInvoices.buildUrl());
        
        return new UrlRequestCmd<ODataModelResponseV4<Job>>(client,ODataHttpMethods.Post,url, undefined,{ headers: getDefaultHeaders(), mainResponseConverter: this._QGenerateGraveInvoices.getResponseConverter()});
    }

    public SetInvoicePaid() {
        if(!this._QSetInvoicePaid) {
          this._QSetInvoicePaid = new QSetInvoicePaid()
        }

        const { addFullPath, client, getDefaultHeaders } = this.__base;
        const url = addFullPath(this._QSetInvoicePaid.buildUrl());
        
        return new UrlRequestCmd<ODataModelResponseV4<Invoice>>(client,ODataHttpMethods.Post,url, undefined,{ headers: getDefaultHeaders(), mainResponseConverter: this._QSetInvoicePaid.getResponseConverter()});
    }
}
