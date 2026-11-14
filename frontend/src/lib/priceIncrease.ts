import type { ItemGraves, PriceIncreaseCorrection } from "@/api/endpoints";
import { formatAmount, type Translate } from "./format";

/** One row of the price increase: the default price, or one own price. */
export interface IncreaseRow {
  isDefault: boolean;
  oldPrice: number;
  graveIds: number[];
  graveCount: number;
  ticked: boolean;
  newPrice: number;
}

/** An increase the user confirmed in the popover and the dialog has yet to save. */
export interface PendingIncrease {
  percent: number | null;
  rows: IncreaseRow[];
  corrections: PriceIncreaseCorrection[];
}

/** A row's name as the dialog's grave section names a group. */
export function rowLabel(row: IncreaseRow, t: Translate): string {
  const price = t(row.isDefault ? "itemPriceDefault" : "itemPriceOwn", [
    formatAmount(row.oldPrice),
  ]);
  return price;
}

/** Whether a ticked row's price is actually going to change. */
export function rowChanged(row: IncreaseRow): boolean {
  return row.ticked && row.newPrice !== row.oldPrice;
}

/** `price` raised by `percent` and rounded to the cent, a midpoint up (prices
 *  are never negative), in integer cents so 10,10 + 5 % does not drift. */
export function raiseByPercent(price: number, percent: number): number {
  const cents = Math.round(price * 100);
  const basis = Math.round(percent * 100);
  const raised = cents * (10000 + basis);
  return Math.floor((raised + 5000) / 10000) / 100;
}

/** The rows of an item's graves: the default price first (merging net and
 *  groups, even with no grave), then one per own price. */
export function increaseRows(graves: ItemGraves): IncreaseRow[] {
  const rows: IncreaseRow[] = [];
  if (graves.DefaultPrice != null) {
    const ids = new Set<number>();
    for (const group of graves.Groups) {
      if (group.IsDefault) {
        group.Graves.forEach((grave) => ids.add(grave.Id));
      }
    }
    rows.push({
      isDefault: true,
      oldPrice: graves.DefaultPrice,
      graveIds: [...ids],
      graveCount: ids.size,
      ticked: true,
      newPrice: graves.DefaultPrice,
    });
  }
  for (const group of graves.Groups) {
    if (group.IsDefault || group.UnitPrice == null) {
      continue;
    }
    const ids = [...new Set(group.Graves.map((grave) => grave.Id))];
    rows.push({
      isDefault: false,
      oldPrice: group.UnitPrice,
      graveIds: ids,
      graveCount: ids.length,
      ticked: true,
      newPrice: group.UnitPrice,
    });
  }
  return rows;
}
