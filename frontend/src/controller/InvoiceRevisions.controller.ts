import JSONModel from "sap/ui/model/json/JSONModel";
import * as api from "../model/api";
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
  protected async loadChain(rootId: number): Promise<any[]> {
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

  /** The model the header block and the revision panels bind to. */
  protected buildRevisionModel(chain: any[]): JSONModel {
    const revisions = chain.map((invoice, index) => {
      const lines = ((invoice.Lines as any[]) ?? [])
        .slice()
        .sort((a, b) => Number(a.LineNumber) - Number(b.LineNumber))
        .map((line) => {
          // the OData V4 model serialises Edm.Decimal as a string
          const amounts = {
            Count: Number(line.Count),
            UnitPrice: Number(line.UnitPrice),
            VatRate: Number(line.VatRate),
          };
          return {
            ...amounts,
            ItemNumber: line.ItemNumber,
            Description: line.Description,
            LineTotal: api.lineTotal(amounts),
          };
        });
      const totals = api.invoiceTotals(lines);
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
        NetTotal: totals.net,
        VatTotal: totals.vat,
        GrossTotal: totals.gross,
      };
    });

    const latest = chain[0];
    return new JSONModel({
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
    });
  }
}
