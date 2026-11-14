import type { Invoice, InvoiceLine, Quote, QuoteLine } from "@/api/generated";
import { grossPerLine, lineNet, splitGross } from "./money";

export interface RevisionLineView {
  ItemNumber: string;
  Description: string;
  Count: number;
  UnitPrice: number;
  /** What a line entered VAT-included was stored at; null on every other. */
  GrossUnitPrice: number | null;
  VatRate: number;
  PriceIncludesVat: boolean;
  /** The line's net, rounded to the cent: count × UnitPrice, or on a gross-led
   *  line what is left of count × GrossUnitPrice after its VAT. */
  LineTotal: number;
  /** This line's part of what is actually charged, gross (grossPerLine). */
  GrossTotal: number;
}

export interface RevisionView {
  Id: number;
  /** The version's own number, with its revision marker: an invoice's or a
   *  quote's. */
  Number: string;
  RevisionNumber: number;
  GeneratedAt: string;
  Lines: RevisionLineView[];
  NetTotal: number;
  VatTotal: number;
  GrossTotal: number;
}

/** What an invoice and a quote version have in common for the panel. */
interface VersionRow {
  Id: number;
  RevisionNumber: number;
  GeneratedAt: string;
  NetTotal: number;
  VatTotal: number;
  GrossTotal: number;
}

function buildView(
  row: VersionRow,
  number: string,
  rowLines: (InvoiceLine | QuoteLine)[],
  recipientCount: number,
): RevisionView {
  const lines: RevisionLineView[] = rowLines
    .slice()
    .sort((a, b) => a.LineNumber - b.LineNumber)
    .map((line) => ({
      ItemNumber: line.ItemNumber,
      Description: line.Description,
      Count: line.Count,
      UnitPrice: line.UnitPrice,
      GrossUnitPrice: line.GrossUnitPrice ?? null,
      VatRate: line.VatRate,
      PriceIncludesVat: line.PriceIncludesVat,
      LineTotal:
        line.GrossUnitPrice != null
          ? splitGross(lineNet(line.Count, line.GrossUnitPrice), line.VatRate).net
          : lineNet(line.Count, line.UnitPrice),
      GrossTotal: 0,
    }));
  const shares = grossPerLine(
    lines.map((line) => ({ NetTotal: line.LineTotal, VatRate: line.VatRate })),
    recipientCount,
  );
  lines.forEach((line, index) => {
    line.GrossTotal = shares[index];
  });
  return {
    Id: row.Id,
    Number: number,
    RevisionNumber: row.RevisionNumber,
    GeneratedAt: row.GeneratedAt,
    Lines: lines,
    NetTotal: row.NetTotal,
    VatTotal: row.VatTotal,
    GrossTotal: row.GrossTotal,
  };
}

/** Newest first, as loadChain returns them. On a grave invoice the lines are
 *  the whole grave while the totals are this recipient's share, so the
 *  recipient count is the divisor that puts the gross column in the net
 *  column's terms. */
export function buildRevisions(chain: Invoice[]): RevisionView[] {
  return chain.map((invoice) =>
    buildView(invoice, invoice.InvoiceNumber, invoice.Lines ?? [], invoice.RecipientCount ?? 1),
  );
}

/** Newest first, as loadQuoteChain returns them. A quote has one recipient,
 *  who is charged the whole of every line. */
export function buildQuoteRevisions(chain: Quote[]): RevisionView[] {
  return chain.map((quote) => buildView(quote, quote.QuoteNumber, quote.Lines ?? [], 1));
}
