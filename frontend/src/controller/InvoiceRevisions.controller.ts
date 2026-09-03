import * as api from "../model/api";
import {
  emptyRevisionDetail,
  type Revision,
  type RevisionDetailView,
  type RevisionLine,
} from "../model/state";
import BaseController from "./Base.controller";

/**
 * Loads an invoice chain — every revision with its lines, documents and totals —
 * for the two detail pages. They render different markup around it, so only the
 * loading is shared.
 */
export default abstract class InvoiceRevisions extends BaseController {
  /**
   * The whole chain, newest revision first. Takes the chain's root id, which is
   * what every list row carries; a revision's id pasted into the URL resolves
   * through one extra read rather than showing an empty page.
   */
  protected async loadChain(rootId: number): Promise<api.InvoiceRow[]> {
    const parameters = {
      $orderby: "RevisionNumber desc",
      $expand: "Lines,Documents($select=Id,FileName,Kind)",
    };

    const rows = await this.readList<api.InvoiceRow>("/Invoices", {
      ...parameters,
      $filter: `RootId eq ${rootId}`,
    });
    if (rows.length) {
      return rows;
    }

    const single = await this.readList<api.InvoiceRow>("/Invoices", {
      $filter: `Id eq ${rootId}`,
    });
    if (!single.length) {
      return [];
    }

    return this.readList<api.InvoiceRow>("/Invoices", {
      ...parameters,
      $filter: `RootId eq ${Number(single[0].RootId)}`,
    });
  }

  /** What the header block and the revision panels bind to. */
  protected buildRevisionDetail(chain: api.InvoiceRow[]): RevisionDetailView {
    const revisions: Revision[] = chain.map((invoice, index) => {
      const lines: RevisionLine[] = (invoice.Lines ?? [])
        .slice()
        .sort((a, b) => Number(a.LineNumber) - Number(b.LineNumber))
        .map((line) => {
          // the OData V4 model serialises Edm.Decimal as a string
          const amounts = {
            Count: Number(line.Count),
            UnitPrice: Number(line.UnitPrice),
            VatRate: Number(line.VatRate),
          };
          // the line as printed — Count × UnitPrice, rounded to the cent the
          // way the document rounds it — not affected by a grave's share,
          // which only scales the invoice-level totals below
          const lineTotal = Math.round(amounts.Count * amounts.UnitPrice * 100) / 100;
          return {
            ...amounts,
            ItemNumber: line.ItemNumber,
            Description: line.Description,
            PriceIncludesVat: Boolean(line.PriceIncludesVat),
            LineTotal: lineTotal,
            // Derived per line, which is not how the invoice adds VAT: it rounds
            // once per rate, so these need not sum to the gross below. The note
            // under the table says as much — the summary is what reconciles.
            GrossTotal: api.grossFromNet(lineTotal, amounts.VatRate),
          };
        });
      const revisionNumber = Number(invoice.RevisionNumber);

      return {
        Id: Number(invoice.Id),
        InvoiceNumber: invoice.InvoiceNumber,
        RevisionNumber: revisionNumber,
        GeneratedAt: invoice.GeneratedAt,
        Title:
          revisionNumber === 0
            ? this.i18nText("revisionOriginal")
            : this.i18nText("revisionLabel").replace("{0}", String(revisionNumber)),
        // the current revision opens; the history stays collapsed
        Expanded: index === 0,
        Lines: lines,
        NetTotal: Number(invoice.NetTotal),
        VatTotal: Number(invoice.VatTotal),
        GrossTotal: Number(invoice.GrossTotal),
      };
    });

    const latest = chain[0];
    return {
      ...emptyRevisionDetail(),
      invoiceNumber: this.formatInvoiceNumber(latest.InvoiceNumber),
      recipientName: latest.RecipientName,
      recipientAddress: latest.RecipientAddress,
      // the edit dialog reopens on this recipient
      recipientId: Number(latest.RecipientId),
      latestInvoiceId: Number(latest.Id),
      graveId: latest.GraveId != null ? Number(latest.GraveId) : null,
      periodYear: latest.PeriodYear != null ? Number(latest.PeriodYear) : null,
      periodHalf: latest.PeriodHalf != null ? Number(latest.PeriodHalf) : null,
      halfYear:
        latest.PeriodYear != null && latest.PeriodHalf != null
          ? api.halfYearText(Number(latest.PeriodYear), Number(latest.PeriodHalf))
          : "",
      sharePercent: latest.SharePercent != null ? Number(latest.SharePercent) : null,
      revisions,
      paidOn: latest.PaidOn,
    };
  }

  /**
   * Each item's current VAT rate, indexed by id, for the price fields. Both edit
   * dialogs already bind the whole catalogue into their item ComboBox, so this is
   * one more request rather than more data — and it has to be the catalogue
   * rather than the line, because the backend converts what is sent back using
   * the item's current rate.
   */
  protected async loadItemVatRates(): Promise<Map<number, number>> {
    const items = await this.readList<{ Id: number | string; VatRate: number | string }>(
      "/InvoiceItems",
      { $select: "Id,VatRate" },
    );

    return new Map(items.map((item) => [Number(item.Id), Number(item.VatRate)]));
  }
}
