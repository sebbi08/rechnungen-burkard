import type { InvoiceLineInput } from "@/api/endpoints";
import { ApiError } from "@/api/http";

/** One row of a line editor. `unitPrice` is what the field shows: the gross
 *  when `priceIncludesVat`, the net otherwise; null means "no price entered". */
export interface DraftLine {
  key: number;
  itemId: number | null;
  count: number | null;
  unitPrice: number | null;
  priceIncludesVat: boolean;
  /** The note under the line on an ordinary invoice or a quote; a grave's
   *  lines have none. */
  comment: string;
}

let nextKey = 1;

export function newLine(init: Partial<Omit<DraftLine, "key">> = {}): DraftLine {
  return {
    key: nextKey++,
    itemId: null,
    count: 1,
    unitPrice: null,
    priceIncludesVat: false,
    comment: "",
    ...init,
  };
}

/** The payload for any line-taking endpoint. Every line needs an item
 *  (ItemRequired) and a whole count above zero (InvalidCount; the field lets a
 *  typed "1,5" stand so it can be refused here). An empty price is sent as
 *  null -- the item's default is billed -- while 0 is kept as a real price.
 *  Comments go along only where the document prints them: ordinary invoices
 *  and quotes, never a grave. */
export function toLineInputs(
  lines: DraftLine[],
  { withComments = false }: { withComments?: boolean } = {},
): InvoiceLineInput[] {
  return lines.map((line) => {
    if (line.itemId == null) {
      throw new ApiError("ItemRequired");
    }
    if (line.count == null || !Number.isInteger(line.count) || line.count <= 0) {
      throw new ApiError("InvalidCount");
    }
    return {
      itemId: line.itemId,
      count: line.count,
      unitPrice: line.unitPrice,
      priceIncludesVat: line.priceIncludesVat,
      ...(withComments ? { comment: line.comment } : {}),
    };
  });
}
