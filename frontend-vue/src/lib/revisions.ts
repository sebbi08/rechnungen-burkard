import type { Invoice } from "@/api/generated";
import { grossPerLine, lineNet } from "./money";

export interface RevisionLineView {
  ItemNumber: string;
  Description: string;
  Count: number;
  UnitPrice: number;
  VatRate: number;
  PriceIncludesVat: boolean;
  /** Count × UnitPrice as printed, rounded to the cent. */
  LineTotal: number;
  /** This line's part of what is actually charged, gross (grossPerLine). */
  GrossTotal: number;
}

export interface RevisionView {
  Id: number;
  InvoiceNumber: string;
  RevisionNumber: number;
  GeneratedAt: string;
  Lines: RevisionLineView[];
  NetTotal: number;
  VatTotal: number;
  GrossTotal: number;
}

/** Newest first, as loadChain returns them. On a grave invoice the lines are
 *  the whole grave while the totals are this recipient's share, so the
 *  recipient count is the divisor that puts the gross column in the net
 *  column's terms. Port of InvoiceRevisions.controller.ts. */
export function buildRevisions(chain: Invoice[]): RevisionView[] {
  return chain.map((invoice) => {
    const lines: RevisionLineView[] = (invoice.Lines ?? [])
      .slice()
      .sort((a, b) => a.LineNumber - b.LineNumber)
      .map((line) => ({
        ItemNumber: line.ItemNumber,
        Description: line.Description,
        Count: line.Count,
        UnitPrice: line.UnitPrice,
        VatRate: line.VatRate,
        PriceIncludesVat: line.PriceIncludesVat,
        LineTotal: lineNet(line.Count, line.UnitPrice),
        GrossTotal: 0,
      }));
    const shares = grossPerLine(
      lines.map((line) => ({ NetTotal: line.LineTotal, VatRate: line.VatRate })),
      invoice.RecipientCount ?? 1,
    );
    lines.forEach((line, index) => {
      line.GrossTotal = shares[index];
    });
    return {
      Id: invoice.Id,
      InvoiceNumber: invoice.InvoiceNumber,
      RevisionNumber: invoice.RevisionNumber,
      GeneratedAt: invoice.GeneratedAt,
      Lines: lines,
      NetTotal: invoice.NetTotal,
      VatTotal: invoice.VatTotal,
      GrossTotal: invoice.GrossTotal,
    };
  });
}
