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
}

let nextKey = 1;

export function newLine(init: Partial<Omit<DraftLine, "key">> = {}): DraftLine {
  return {
    key: nextKey++,
    itemId: null,
    count: 1,
    unitPrice: null,
    priceIncludesVat: false,
    ...init,
  };
}

/** The payload for any line-taking endpoint. Every line needs an item
 *  (ItemRequired). An empty price is sent as null -- the item's default is
 *  billed -- while 0 is kept as a real price. */
export function toLineInputs(lines: DraftLine[]): InvoiceLineInput[] {
  return lines.map((line) => {
    if (line.itemId == null) {
      throw new ApiError("ItemRequired");
    }
    return {
      itemId: line.itemId,
      count: line.count ?? 0,
      unitPrice: line.unitPrice,
      priceIncludesVat: line.priceIncludesVat,
    };
  });
}
