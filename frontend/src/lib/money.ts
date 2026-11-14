/** The gross a price field shows for a stored net: rounds to the cent,
 *  half-up on a double, which equals the backend's away-from-zero rounding for
 *  every price this application deals in. The backend has no counterpart
 *  function to mirror — the inverse rule lives only in
 *  `VatInclusivePriceTests.The_displayed_gross_converts_back_to_the_same_net`
 *  — so a future change to `NetOf`'s rounding has nothing forcing this to
 *  follow it. The two rules diverge on an exact half-cent tie, but only on a
 *  negative one: `Math.round` takes a .5 towards +Infinity, away-from-zero takes
 *  it away from it, and a price is never negative. They can also diverge where a
 *  binary double lands just under a value the decimal arithmetic treats as a
 *  tie. Neither is reachable at the rates this application uses: over every
 *  `NetOf` output for a gross of 0,01 to 2000,00, no rate in the settings
 *  produces a disagreement. The sign is what rules the tie out. */
export function grossFromNet(net: number, vatRate: number): number {
  return Math.round(net * (1 + vatRate / 100) * 100) / 100;
}

/** One line's net and VAT rate, for `grossPerLine` below. */
export interface GrossLine {
  NetTotal: number;
  VatRate: number;
}

/** What each line accounts for, gross, so the column adds up to what the
 *  recipients are actually charged.
 *
 *  Adding VAT to each line on its own does not reach that figure: it is rounded
 *  once per VAT rate rather than per line, and on a grave once per recipient on
 *  top. So each rate's charge is worked out the way it is billed — an equal
 *  share per recipient, VAT rounded on the share — and then divided across that
 *  rate's own lines, largest remainder first. Ties keep the earlier line, so the
 *  same figures always produce the same rows.
 *
 *  Rate by rate, never across the whole table. Sharing one blended total out
 *  over every line gave a 7 % line the same multiplier as a 19 % one: 100,00 €
 *  net showed 112,43 € gross beside its own "7 %". No line is now more than a
 *  cent from its own net plus VAT.
 *
 *  This decides no amount. The prices are already fixed and already divide — a
 *  grave that did not could not have been saved — so this only splits a figure
 *  the backend settled, which is why it can live in the browser. */
export function grossPerLine(lines: GrossLine[], recipients: number): number[] {
  const result = lines.map(() => 0);
  const divisor = Math.max(1, Math.round(recipients));

  for (const rate of new Set(lines.map((line) => line.VatRate))) {
    const indexes = lines
      .map((line, index) => ({ line, index }))
      .filter((entry) => entry.line.VatRate === rate)
      .map((entry) => entry.index);

    const netCents = indexes.map((index) => Math.round(lines[index].NetTotal * 100));
    const subtotal = netCents.reduce((sum, value) => sum + value, 0);
    if (subtotal === 0) {
      continue;
    }

    // what this rate is charged: each recipient an equal share of the subtotal
    // plus that share's own rounded VAT
    const share = Math.round(subtotal / divisor);
    const charged = divisor * (share + Math.round((share * rate) / 100));

    const exact = netCents.map((net) => (net * charged) / subtotal);
    const shares = exact.map((value) => Math.floor(value));
    const order = exact
      .map((value, position) => ({ position, fraction: value - Math.floor(value) }))
      .sort((a, b) => b.fraction - a.fraction || a.position - b.position);

    let left = charged - shares.reduce((sum, value) => sum + value, 0);
    for (let step = 0; left > 0; step++, left--) {
      shares[order[step % order.length].position] += 1;
    }

    indexes.forEach((index, position) => {
      result[index] = shares[position] / 100;
    });
  }

  return result;
}

/** What a price field shows for a line's stored net price: the gross for a line
 *  entered VAT-included, and the untouched net for every other one. Every
 *  price has two decimals, on a grave line as anywhere else. The rate is the
 *  item's current one, not the line's stored one: the backend converts what the dialog
 *  sends using the item as it is now, so displaying with an older rate would
 *  move the price of a line nobody touched. */
export function priceFieldValue(net: number, priceIncludesVat: boolean, vatRate: number): number {
  return priceIncludesVat ? grossFromNet(net, vatRate) : net;
}

/** Count × price as the document prints it: rounded to the cent, a half cent
 *  away from zero, as the backend's decimals do. Worked in whole ten-thousandths
 *  of a euro -- a price has at most four decimals -- since a binary float rounds
 *  3 × 1,005 to 3,01 where the document says 3,02. */
export function lineNet(count: number, unitPrice: number): number {
  const total = count * Math.round(unitPrice * 10000);
  const cents = Math.sign(total) * Math.floor((Math.abs(total) + 50) / 100);
  return cents / 100;
}

/** An amount rounded to the cent, a half cent away from zero. */
export function roundCents(amount: number): number {
  return lineNet(1, amount);
}

/** A line as the document prints it. */
export interface PrintedLine {
  UnitPrice: number;
  GrossUnitPrice: number | null;
  PriceIncludesVat: boolean;
  VatRate: number;
}

/** VAT and net inside a gross line total, the way the backend splits it:
 *  VAT is the share of the gross, rounded once, a half cent away from zero, and
 *  the net is what is left, so the two always add up to the gross.
 *
 *  Worked in integers -- the gross in whole cents, the rate in thousandths of a
 *  percent -- and rounded a single time. Going through `roundCents` rounded to
 *  four decimals first and then to cents, which turned a VAT of 0,114957 into
 *  0,12 where the backend's decimal says 0,11. */
export function splitGross(lineGross: number, vatRate: number): { net: number; vat: number } {
  const grossCents = Math.round(lineGross * 100);
  const rate = Math.round(vatRate * 1000);
  const divisor = 100000 + rate;
  const vatCents =
    Math.sign(grossCents) * Math.floor((2 * Math.abs(grossCents) * rate + divisor) / (2 * divisor));
  return { net: (grossCents - vatCents) / 100, vat: vatCents / 100 };
}

/** The unit price the PDF prints. A line entered VAT-included has its gross
 *  stored and printed as entered; an older flagged line has only a net, which
 *  is shown grossed up; a net line prints its net. */
export function printedUnitPrice(line: PrintedLine): number {
  if (line.GrossUnitPrice != null) {
    return line.GrossUnitPrice;
  }
  return line.PriceIncludesVat ? grossFromNet(line.UnitPrice, line.VatRate) : line.UnitPrice;
}
