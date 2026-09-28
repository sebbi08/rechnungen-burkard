import type { ODataModelResponseV4, ODataCollectionResponseV4 } from "@odata2ts/odata-core";
import {
  QAction,
  ModelResponseConverterV4,
  CollectionResponseConverterV4,
} from "@odata2ts/odata-query-objects";
import type { Invoice } from "./burkard-invoicing-domain-entities/invoice/Invoice.js";
import type { Job } from "./burkard-invoicing-domain-entities/job/Job.js";
import { QInvoice } from "./burkard-invoicing-domain-entities/invoice/QInvoice.js";
import { QJob } from "./burkard-invoicing-domain-entities/job/QJob.js";

export class QCreateInvoice extends QAction<undefined, ODataModelResponseV4<Invoice>> {
  private readonly params: [] = [];

  constructor() {
    super("CreateInvoice", new ModelResponseConverterV4(new QInvoice()));
  }

  getParams() {
    return this.params;
  }
}

export class QCreateRevision extends QAction<undefined, ODataModelResponseV4<Invoice>> {
  private readonly params: [] = [];

  constructor() {
    super("CreateRevision", new ModelResponseConverterV4(new QInvoice()));
  }

  getParams() {
    return this.params;
  }
}

export class QCreateGraveInvoiceRevision extends QAction<
  undefined,
  ODataCollectionResponseV4<Invoice>
> {
  private readonly params: [] = [];

  constructor() {
    super("CreateGraveInvoiceRevision", new CollectionResponseConverterV4(new QInvoice()));
  }

  getParams() {
    return this.params;
  }
}

export class QGenerateGraveInvoices extends QAction<undefined, ODataModelResponseV4<Job>> {
  private readonly params: [] = [];

  constructor() {
    super("GenerateGraveInvoices", new ModelResponseConverterV4(new QJob()));
  }

  getParams() {
    return this.params;
  }
}

export class QSetInvoicePaid extends QAction<undefined, ODataModelResponseV4<Invoice>> {
  private readonly params: [] = [];

  constructor() {
    super("SetInvoicePaid", new ModelResponseConverterV4(new QInvoice()));
  }

  getParams() {
    return this.params;
  }
}
