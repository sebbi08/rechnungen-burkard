import type { Button$PressEvent } from "sap/m/Button";
import type { Route$PatternMatchedEvent } from "sap/ui/core/routing/Route";
import * as api from "../model/api";
import GraveEdit from "./GraveEdit.controller";

/** One of the grave's item links, as the page lists them.
 *
 *  Every figure here is what the grave is actually billed. A grave whose prices
 *  did not divide evenly across its recipients could not have been saved, so
 *  nothing shown is an approximation of something else. */
interface GraveItemRow {
  Number: string;
  Description: string;
  Count: number;
  /** The unit price in the unit it was entered in: the gross for a VAT-included
   *  line, the net for every other one. */
  Price: number;
  /** Count × the billed net. */
  NetTotal: number | null;
  /** The share of what the recipients collect that this line accounts for.
   *  Null on a grave with no recipients, where nothing is billed at all. */
  GrossTotal: number | null;
  VatRate: number;
  PriceIncludesVat: boolean;
}

/** Fills in what each line accounts for out of `total`, the gross the
 *  recipients collect between them. Null when nothing is charged at all — a
 *  grave with no recipients — and the column then stays empty. */
function shareGross(rows: GraveItemRow[], total: number | null): GraveItemRow[] {
  const priced = rows.filter((row) => row.NetTotal !== null);
  if (total === null || priced.length === 0) {
    return rows;
  }

  const shares = api.shareOfTotal(
    priced.map((row) => row.NetTotal!),
    total,
  );
  priced.forEach((row, index) => {
    row.GrossTotal = shares[index];
  });
  return rows;
}

interface GraveRecipientRow {
  Name: string;
  Address: string;
}

/** One invoice the grave has produced, summarised for the list. */
interface GraveInvoiceRow {
  Id: number;
  RootId: number;
  HalfYear: string;
  RecipientName: string;
  InvoiceNumber: string;
  RevisionNumber: number;
  GeneratedAt: string;
  GrossTotal: number;
  /** Set when this recipient's invoice is paid; the date itself is never shown. */
  PaidOn: string | null;
}

/** Everything this page shows about one grave. */
interface GraveDetailView {
  Id: number;
  GraveNumber: string;
  StartDate: string;
  EndDate: string;
  CreatedAt: string;
  currentHalfYearLabel: string;
  currentStatusText: string;
  currentStatusAnnouncement: string;
  currentInvoiced: boolean;
  Items: GraveItemRow[];
  Recipients: GraveRecipientRow[];
  Invoices: GraveInvoiceRow[];
}

/** One grave: its configuration, this half-year's status, and every invoice it
 *  has produced across all half-years and recipients. */
export default class GraveDetail extends GraveEdit {
  public onInit(): void {
    this.bindODataRoot();
    this.router()
      .getRoute("graveDetail")!
      // wrapped rather than passed directly: the handler is async and the
      // event expects a void return
      .attachPatternMatched((event) => void this.onRouteMatched(event));
  }

  private async onRouteMatched(event: Route$PatternMatchedEvent): Promise<void> {
    const args = event.getParameter("arguments") as { graveId: string };
    this._graveId = Number(args.graveId);
    await this.refresh();
  }

  protected async refresh(): Promise<void> {
    try {
      const [details, runs, invoices] = await Promise.all([
        api.fetchGraveDetails(this._graveId),
        this.readList<api.GraveInvoiceRunRow>("/GraveInvoiceRuns", {
          $filter: `GraveId eq ${this._graveId}`,
        }),
        this.readList<api.InvoiceRow>("/Invoices", {
          $filter: `GraveId eq ${this._graveId} and IsLatestRevision eq true`,
          $orderby: "PeriodYear desc,PeriodHalf desc,RecipientName",
        }),
      ]);

      const [year, half] = api.currentHalfYear();
      const invoiced = runs.some((run) => Number(run.Year) === year && Number(run.Half) === half);

      // the grave's own lines as this half-year actually billed them, when it
      // has been invoiced — a per-VAT-rate subtotal that did not divide evenly
      // among recipients can carry a price a cent higher than the grave's
      // configuration, and the page should show the same figure the invoices
      // do rather than the unadjusted one. Any of this half-year's invoices
      // names the same group, since they all carry the grave's own lines at
      // 100 %; falling back to the grave's configuration when none exists yet.
      const currentInvoice = invoices.find(
        (invoice) => Number(invoice.PeriodYear) === year && Number(invoice.PeriodHalf) === half,
      );
      // Rows come from this half-year's invoices where it has been billed, and
      // from the grave's own configuration where it has not. Both are exact: a
      // grave whose prices could not be split evenly across its recipients
      // could not have been saved, so the configuration bills as it stands.
      const check = currentInvoice ? null : await this.billedLines(details);
      const rows: GraveItemRow[] = currentInvoice
        ? (await api.fetchGraveInvoiceGroup(Number(currentInvoice.Id))).Lines.map((line) => ({
            Number: line.ItemNumber,
            Description: line.Description,
            Count: Number(line.Count),
            Price: api.priceFieldValue(
              Number(line.UnitPrice),
              Boolean(line.PriceIncludesVat),
              Number(line.VatRate),
            ),
            NetTotal: Math.round(Number(line.UnitPrice) * Number(line.Count) * 100) / 100,
            GrossTotal: null,
            VatRate: Number(line.VatRate),
            PriceIncludesVat: Boolean(line.PriceIncludesVat),
          }))
        : this.configuredItems(details, check);

      // The gross each line accounts for, out of what the recipients collect
      // between them. Read off the invoices where they exist, so the table
      // cannot disagree with the list below it, and off the same check the save
      // uses where they do not.
      const items = shareGross(
        rows,
        currentInvoice
          ? invoices
              .filter(
                (invoice) =>
                  Number(invoice.PeriodYear) === year && Number(invoice.PeriodHalf) === half,
              )
              .reduce((sum, invoice) => sum + Number(invoice.GrossTotal ?? 0), 0)
          : (check?.GrossTotal ?? null),
      );

      this.setState<GraveDetailView>("grave", {
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
        Items: items,
        Recipients: details.Recipients.map((link) => ({
          Name: link.Recipient.Name,
          Address: link.Recipient.Address,
        })),
        Invoices: invoices.map((invoice) => ({
          Id: Number(invoice.Id),
          RootId: Number(invoice.RootId),
          HalfYear: api.halfYearText(Number(invoice.PeriodYear), Number(invoice.PeriodHalf)),
          RecipientName: invoice.RecipientName,
          InvoiceNumber: this.formatInvoiceNumber(invoice.InvoiceNumber),
          RevisionNumber: Number(invoice.RevisionNumber),
          GeneratedAt: invoice.GeneratedAt,
          // the OData V4 model serialises Edm.Decimal as a string
          GrossTotal: Number(invoice.GrossTotal),
          PaidOn: invoice.PaidOn,
        })),
      });
    } catch (error) {
      api.showError(this, error);
    }
  }

  /** What this grave would be billed, for a half-year that has not been billed
   *  yet. Null when there is nothing to bill — no recipients, no items — or
   *  when the check could not be reached; the page then leaves the derived
   *  columns empty rather than naming a figure nobody is charged.
   *
   *  It is asked of the backend rather than worked out here: the same rule
   *  decides what the save accepts, and a second copy of it would be free to
   *  disagree with the one that refuses. */
  private async billedLines(details: api.GraveDetails): Promise<api.GraveBillingCheck | null> {
    if (details.Recipients.length === 0 || details.Items.length === 0) {
      return null;
    }

    try {
      return await api.checkGraveBilling(
        details.Recipients.length,
        details.Items.map((link) => ({
          ItemId: Number(link.Item.Id),
          Count: link.Count,
          UnitPrice: link.UnitPrice === null ? null : Number(link.UnitPrice),
          PriceIncludesVat: link.PriceIncludesVat,
        })),
      );
    } catch {
      return null;
    }
  }

  /** The grave's items as configured, for a half-year that has not been billed
   *  yet. Exact by construction: a grave whose prices could not be split evenly
   *  could not have been saved. */
  private configuredItems(
    details: api.GraveDetails,
    check: api.GraveBillingCheck | null,
  ): GraveItemRow[] {
    return details.Items.map((link, index) => ({
      Number: link.Item.Number,
      Description: link.Item.Description,
      Count: link.Count,
      // the link's own price wins; without one the item's default is billed.
      // Stored as typed -- already the gross on a VAT-included line -- so it is
      // shown as it is rather than converted a second time.
      Price: Number(link.UnitPrice ?? link.Item.DefaultPrice ?? 0),
      NetTotal: check ? Number(check.Lines[index].NetTotal) : null,
      GrossTotal: null,
      VatRate: link.Item.VatRate,
      PriceIncludesVat: link.PriceIncludesVat,
    }));
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

  public onShowInvoiceDetails(event: Button$PressEvent): void {
    const context = event.getSource().getBindingContext("grave")!;
    this.router().navTo("graveInvoiceDetail", {
      graveId: String(this._graveId),
      rootId: String(context.getProperty("RootId")),
    });
  }

  public async onDownloadInvoiceNormal(event: Button$PressEvent): Promise<void> {
    await this.downloadRow(event, false);
  }

  public async onDownloadInvoiceCopy(event: Button$PressEvent): Promise<void> {
    await this.downloadRow(event, true);
  }

  private async downloadRow(event: Button$PressEvent, isCopy: boolean): Promise<void> {
    const context = event.getSource().getBindingContext("grave")!;
    await this.openDocument(Number(context.getProperty("Id")), isCopy);
  }

  public async onEditGraveInvoice(event: Button$PressEvent): Promise<void> {
    const context = event.getSource().getBindingContext("grave")!;
    await this.openGraveInvoiceDialog(Number(context.getProperty("Id")));
  }

  public async onToggleInvoicePaid(event: Button$PressEvent): Promise<void> {
    const context = event.getSource().getBindingContext("grave")!;
    const changed = await this.togglePaid(
      Number(context.getProperty("Id")),
      context.getProperty("PaidOn") as string | null,
    );
    if (changed) {
      // the page's header status and the row are built from one read
      await this.refresh();
    }
  }

  private _graveId = 0;
}
