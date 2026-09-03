import type { Button$PressEvent } from "sap/m/Button";
import type { Route$PatternMatchedEvent } from "sap/ui/core/routing/Route";
import * as api from "../model/api";
import GraveEdit from "./GraveEdit.controller";

/** One of the grave's item links, as the page lists them. */
interface GraveItemRow {
  Number: string;
  Description: string;
  Count: number;
  /** The unit price, in the unit it was entered in: the gross for a
   *  VAT-included line, the net for every other one. */
  Price: number;
  /** Count × Price, as net and as gross. Gross is per line and therefore
   *  informational — VAT is rounded per rate, not per line. Null on a grave
   *  with no recipients, where no price is billed at all. */
  NetTotal: number | null;
  GrossTotal: number | null;
  VatRate: number;
  PriceIncludesVat: boolean;
  /** What was entered, when billing raised the price above it; null when the
   *  two agree. Shown so the page says why the figure differs from what was
   *  typed. */
  EnteredPrice: number | null;
  /** Drives the warning colouring and the "eingegeben: …" line. */
  PriceWasRaised: boolean;
}

/** The pieces a row is built from, whichever of the three sources it came
 *  from: this half-year's invoices, the billing preview, or — for a grave with
 *  no recipients — the configuration alone. */
interface GraveItemRowInput {
  Number: string;
  Description: string;
  Count: number;
  /** The billed net, or null when nothing would be billed. */
  NetUnitPrice: number | null;
  VatRate: number;
  PriceIncludesVat: boolean;
  /** The price as it was typed, in its own unit. */
  Entered: number | null;
}

/** One row of the grave's item table.
 *
 *  Net and gross are line sums. The gross is derived per line, which is not how
 *  an invoice adds VAT — it rounds once per rate — so the column is a reading
 *  aid rather than a figure anything is reconciled against. `grossRoundingNote`
 *  says so under the table. */
function itemRow(input: GraveItemRowInput): GraveItemRow {
  const price =
    input.NetUnitPrice === null
      ? (input.Entered ?? 0)
      : api.priceFieldValue(input.NetUnitPrice, input.PriceIncludesVat, input.VatRate);
  const netTotal =
    input.NetUnitPrice === null ? null : Math.round(input.NetUnitPrice * input.Count * 100) / 100;
  const raised = input.Entered !== null && input.Entered !== price;

  return {
    Number: input.Number,
    Description: input.Description,
    Count: input.Count,
    Price: price,
    NetTotal: netTotal,
    GrossTotal: netTotal === null ? null : api.grossFromNet(netTotal, input.VatRate),
    VatRate: input.VatRate,
    PriceIncludesVat: input.PriceIncludesVat,
    EnteredPrice: raised ? input.Entered : null,
    PriceWasRaised: raised,
  };
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
      // What the grave holds, by item number — the billed rows below compare
      // themselves against it to show where billing had to raise a price.
      //
      // Taken as stored, with no conversion: a grave keeps the price as it was
      // typed, which for a VAT-included line is already the gross. Running it
      // through priceFieldValue would add the VAT a second time and report a
      // 119,00 € line as having been entered at 127,33 €.
      const configured = new Map(
        details.Items.map((link) => [
          link.Item.Number,
          Number(link.UnitPrice ?? link.Item.DefaultPrice ?? 0),
        ]),
      );

      const items: GraveItemRow[] = currentInvoice
        ? (await api.fetchGraveInvoiceGroup(Number(currentInvoice.Id))).Lines.map((line) =>
            itemRow({
              Number: line.ItemNumber,
              Description: line.Description,
              Count: Number(line.Count),
              NetUnitPrice: Number(line.UnitPrice),
              VatRate: Number(line.VatRate),
              PriceIncludesVat: Boolean(line.PriceIncludesVat),
              // an item dropped from the grave since it was invoiced has nothing
              // left to compare against
              Entered: configured.get(line.ItemNumber) ?? null,
            }),
          )
        : // Not invoiced yet, so the billed prices do not exist and cannot be
          // worked out here: converting a VAT-included price and raising it for
          // the split are backend rules, and how far a price is raised depends
          // on how many recipients the grave has. The preview answers with the
          // figures generation would produce. Without recipients there is no
          // billed price at all, and the page says so rather than inventing one.
          await this.previewItems(details);

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

  /** The grave's items as they would be billed, for a half-year that has not
   *  been invoiced yet.
   *
   *  How far a price is raised depends on the recipient count, so a grave with
   *  none has no billed price to show: those rows carry the entered price and
   *  leave the net and gross columns empty rather than name a figure that no
   *  invoice would ever carry. A failing preview does the same — the page is
   *  still readable without the derived columns, and an error strip over a
   *  detail page the user only wanted to look at would be worse. */
  private async previewItems(details: api.GraveDetails): Promise<GraveItemRow[]> {
    const rows = details.Items.map((link) => ({
      Number: link.Item.Number,
      Description: link.Item.Description,
      Count: link.Count,
      VatRate: link.Item.VatRate,
      PriceIncludesVat: link.PriceIncludesVat,
      // the link's own price wins; without one the item's default is billed.
      // Stored as typed — already the gross on a VAT-included line — so it is
      // taken as it is rather than converted a second time.
      Entered: Number(link.UnitPrice ?? link.Item.DefaultPrice ?? 0),
      ItemId: Number(link.Item.Id),
      RawPrice: link.UnitPrice === null ? null : Number(link.UnitPrice),
    }));

    const recipients = details.Recipients.length;
    if (recipients === 0 || rows.length === 0) {
      return rows.map((row) => itemRow({ ...row, NetUnitPrice: null }));
    }

    let billed: api.GraveBillingPreviewLine[];
    try {
      billed = await api.previewGraveBilling(
        recipients,
        rows.map((row) => ({
          ItemId: row.ItemId,
          Count: row.Count,
          UnitPrice: row.RawPrice,
          PriceIncludesVat: row.PriceIncludesVat,
        })),
      );
    } catch {
      return rows.map((row) => itemRow({ ...row, NetUnitPrice: null }));
    }

    return rows.map((row, index) =>
      itemRow({ ...row, NetUnitPrice: Number(billed[index].UnitPrice) }),
    );
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
