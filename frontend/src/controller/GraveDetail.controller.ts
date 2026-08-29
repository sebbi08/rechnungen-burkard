import JSONModel from "sap/ui/model/json/JSONModel";
import * as api from "../model/api";
import GraveEdit from "./GraveEdit.controller";

/** One grave: its configuration, this half-year's status, and every invoice it
 *  has produced across all half-years and recipients. */
export default class GraveDetail extends GraveEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.router()
      .getRoute("graveDetail")!
      .attachPatternMatched(this.onRouteMatched, this);
  }

  private async onRouteMatched(event: any): Promise<void> {
    this._graveId = Number(event.getParameter("arguments").graveId);
    await this.refresh();
  }

  private async refresh(): Promise<void> {
    try {
      const [details, runs, invoices] = await Promise.all([
        api.fetchGraveDetails(this._graveId),
        this.readList<api.GraveInvoiceRunRow>("/GraveInvoiceRuns", {
          $filter: `GraveId eq ${this._graveId}`,
        }),
        this.readList<api.InvoiceRow>("/Invoices", {
          $filter: `GraveId eq ${this._graveId} and IsLatestRevision eq true`,
          $orderby: "PeriodYear desc,PeriodHalf desc,RecipientName",
          $expand: "Lines",
        }),
      ]);

      const now = new Date();
      const year = now.getFullYear();
      // a half-year is Jan–Jun or Jul–Dec
      const half = now.getMonth() < 6 ? 1 : 2;
      const invoiced = runs.some(
        (run) => Number(run.Year) === year && Number(run.Half) === half,
      );

      this.getView()!.setModel(
        new JSONModel({
          Id: details.Id,
          GraveNumber: details.GraveNumber,
          StartDate: details.StartDate,
          EndDate: details.EndDate,
          CreatedAt: details.CreatedAt,
          currentHalfYearLabel: this.i18nText("currentHalfYearStatus").replace(
            "{0}",
            api.halfYearText(year, half),
          ),
          currentStatusText: this.i18nText(invoiced ? "statusInvoiced" : "statusOpen"),
          // without this the red state is announced as a validation error
          currentStatusAnnouncement: this.i18nText(
            invoiced ? "statusInvoicedAnnouncement" : "statusOpenAnnouncement",
          ),
          currentInvoiced: invoiced,
          Items: (details.Items as any[]).map((link) => ({
            Number: link.Item.Number,
            Description: link.Item.Description,
            Count: link.Count,
            // the link's own price wins; without one the item's default is billed
            Price: Number(link.UnitPrice ?? link.Item.DefaultPrice ?? 0),
            VatRate: link.Item.VatRate,
          })),
          Recipients: (details.Recipients as any[]).map((link) => ({
            Name: link.Recipient.Name,
            Address: link.Recipient.Address,
          })),
          Invoices: invoices.map((invoice) => {
            const lines = ((invoice.Lines as any[]) ?? []).map((line) => ({
              // the OData V4 model serialises Edm.Decimal as a string
              Count: Number(line.Count),
              UnitPrice: Number(line.UnitPrice),
              VatRate: Number(line.VatRate),
            }));
            return {
              Id: Number(invoice.Id),
              RootId: Number(invoice.RootId),
              HalfYear: api.halfYearText(
                Number(invoice.PeriodYear),
                Number(invoice.PeriodHalf),
              ),
              RecipientName: invoice.RecipientName,
              InvoiceNumber: this.formatInvoiceNumber(invoice.InvoiceNumber),
              RevisionNumber: Number(invoice.RevisionNumber),
              GeneratedAt: invoice.GeneratedAt,
              GrossTotal: api.invoiceTotals(lines).gross,
            };
          }),
        }),
        "grave",
      );
    } catch (error) {
      api.showError(this, error);
    }
  }

  /** Back to the list this page was opened from, not to the home screen. */
  public onNavBack(): void {
    this.router().navTo("graves");
  }

  public async onEditGrave(): Promise<void> {
    await this.openGraveDialog(this._graveId);
  }

  protected async refreshAfterSave(): Promise<void> {
    await this.refresh();
  }

  public onShowInvoiceDetails(event: any): void {
    const context = event.getSource().getBindingContext("grave");
    this.router().navTo("graveInvoiceDetail", {
      graveId: String(this._graveId),
      rootId: String(context.getProperty("RootId")),
    });
  }

  public async onDownloadInvoiceNormal(event: any): Promise<void> {
    await this.downloadRow(event, false);
  }

  public async onDownloadInvoiceCopy(event: any): Promise<void> {
    await this.downloadRow(event, true);
  }

  private async downloadRow(event: any, isCopy: boolean): Promise<void> {
    const context = event.getSource().getBindingContext("grave");
    await this.openDocument(Number(context.getProperty("Id")), isCopy);
  }

  public async onEditGraveInvoice(event: any): Promise<void> {
    const context = event.getSource().getBindingContext("grave");
    await this.openGraveInvoiceDialog(Number(context.getProperty("Id")));
  }

  private _graveId = 0;
}
