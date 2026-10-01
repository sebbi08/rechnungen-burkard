import { type QueryClient, useQuery } from "@tanstack/vue-query";
import type { Ref } from "vue";
import { currentHalfYear } from "@/lib/halfYear";
import { priceFieldValue } from "@/lib/money";
import { fetchGraveDetails, fetchGraveInvoiceGroup, type GraveDetails } from "./endpoints";
import type { Invoice, InvoiceItem, Recipient } from "./generated";
import { odata } from "./odata";

/** The backend caps $top at 200 (Program.cs, SetMaxTop). */
const PAGE_SIZE = 200;

/** Reads a whole collection in pages the server accepts. */
export async function readAll<T>(page: (skip: number, top: number) => Promise<T[]>): Promise<T[]> {
  const rows: T[] = [];
  for (let skip = 0; ; skip += PAGE_SIZE) {
    const chunk = await page(skip, PAGE_SIZE);
    rows.push(...chunk);
    if (chunk.length < PAGE_SIZE) {
      return rows;
    }
  }
}

export function useVatRatesQuery() {
  return useQuery({
    queryKey: ["vatRates"],
    queryFn: async () =>
      (
        await odata
          .VatRates()
          .query((b, q) => {
            b.orderBy(q.Rate.asc());
          })
          .execute()
      ).data.value,
  });
}

/** A search term as the inside of an OData string literal, for comparing
 *  against tolower(field). odata2ts only wraps a value in quotes, so a quote in
 *  the term ("O'Brien") would end the literal and the backend would answer 400;
 *  OData escapes it by doubling. Only A-Z is lowered: SQLite's lower() leaves
 *  every other letter alone, so a lowered "Ä" would no longer match its own. */
function searchLiteral(term: string): string {
  return term
    .trim()
    .replace(/[A-Z]/g, (letter) => letter.toLowerCase())
    .replaceAll("'", "''");
}

/** Items, newest first; a term filters on number or description, ignoring case. */
export function fetchItems(term: string): Promise<InvoiceItem[]> {
  const literal = searchLiteral(term);
  return readAll(async (skip, top) => {
    const response = await odata
      .InvoiceItems()
      .query((b, q) => {
        if (literal) {
          b.filter(
            q.Number.toLower().contains(literal).or(q.Description.toLower().contains(literal)),
          );
        }
        b.orderBy(q.Id.desc()).skip(skip).top(top);
      })
      .execute();
    return response.data.value;
  });
}

/** Pass `ref("")` for the whole catalogue; pickers share that cache entry. */
export function useItemsQuery(term: Ref<string>) {
  return useQuery({
    queryKey: ["items", "list", term],
    queryFn: () => fetchItems(term.value),
  });
}

/** Each item's current VAT rate. Price fields convert with the item's rate as
 *  it is now, since that is what the backend converts the saved value with. */
export async function loadItemVatRates(queryClient: QueryClient): Promise<Map<number, number>> {
  const items = await queryClient.fetchQuery({
    queryKey: ["items", "list", ""],
    queryFn: () => fetchItems(""),
  });
  return new Map(items.map((item) => [item.Id, item.VatRate]));
}

/** Recipients, newest first; a term filters on the name or the number. */
export function fetchRecipients(term: string): Promise<Recipient[]> {
  const literal = searchLiteral(term);
  return readAll(async (skip, top) => {
    const response = await odata
      .Recipients()
      .query((b, q) => {
        if (literal) {
          b.filter(q.Name.toLower().contains(literal).or(q.Number.toLower().contains(literal)));
        }
        b.orderBy(q.Id.desc()).skip(skip).top(top);
      })
      .execute();
    return response.data.value;
  });
}

export function useRecipientsQuery(term: Ref<string>) {
  return useQuery({
    queryKey: ["recipients", "list", term],
    queryFn: () => fetchRecipients(term.value),
  });
}

export function useSettingsQuery() {
  return useQuery({
    queryKey: ["settings"],
    queryFn: async () => (await odata.Settings().query().execute()).data,
  });
}

/** An invoice chain, newest revision first, lines expanded. Takes the root id
 *  every list row carries; a revision's own id resolves through one extra read. */
export async function loadChain(rootId: number): Promise<Invoice[]> {
  const byRoot = (root: number) =>
    readAll(async (skip, top) => {
      const response = await odata
        .Invoices()
        .query((b, q) => {
          b.filter(q.RootId.eq(root))
            .orderBy(q.RevisionNumber.desc())
            .expanding("Lines", (e) => e)
            .skip(skip)
            .top(top);
        })
        .execute();
      return response.data.value;
    });

  const rows = await byRoot(rootId);
  if (rows.length) {
    return rows;
  }
  const single = (
    await odata
      .Invoices()
      .query((b, q) => {
        b.filter(q.Id.eq(rootId)).select("RootId");
      })
      .execute()
  ).data.value;
  return single.length ? byRoot(single[0].RootId) : [];
}

export interface GraveLineView {
  Number: string;
  Description: string;
  Count: number;
  Price: number;
  VatRate: number;
  PriceIncludesVat: boolean;
}

export interface GraveDetailView {
  grave: GraveDetails;
  /** Whether the current half-year has been invoiced for this grave. */
  currentInvoiced: boolean;
  lines: GraveLineView[];
  /** Latest revisions, newest half-year first, then by recipient. */
  invoices: Invoice[];
}

/** The grave detail page's data. The lines are the current
 *  half-year's as invoiced when it has been, else the grave's configuration
 *  (link price, else the item's default, else 0 -- shown as stored). */
export async function loadGraveDetail(graveId: number): Promise<GraveDetailView> {
  const [grave, runs, invoices] = await Promise.all([
    fetchGraveDetails(graveId),
    readAll(async (skip, top) => {
      const response = await odata
        .GraveInvoiceRuns()
        .query((b, q) => {
          b.filter(q.GraveId.eq(graveId)).skip(skip).top(top);
        })
        .execute();
      return response.data.value;
    }),
    readAll(async (skip, top) => {
      const response = await odata
        .Invoices()
        .query((b, q) => {
          b.filter(q.GraveId.eq(graveId).and(q.IsLatestRevision.isTrue()))
            .orderBy(q.PeriodYear.desc(), q.PeriodHalf.desc(), q.RecipientName.asc())
            .skip(skip)
            .top(top);
        })
        .execute();
      return response.data.value;
    }),
  ]);

  const [year, half] = currentHalfYear();
  const currentInvoiced = runs.some((run) => run.Year === year && run.Half === half);
  const current = invoices.find(
    (invoice) => invoice.PeriodYear === year && invoice.PeriodHalf === half,
  );

  const lines: GraveLineView[] = current
    ? (await fetchGraveInvoiceGroup(current.Id)).Lines.map((line) => ({
        Number: line.ItemNumber,
        Description: line.Description,
        Count: line.Count,
        Price: priceFieldValue(line.UnitPrice, line.PriceIncludesVat, line.VatRate),
        VatRate: line.VatRate,
        PriceIncludesVat: line.PriceIncludesVat,
      }))
    : grave.Items.map((link) => ({
        Number: link.Item.Number,
        Description: link.Item.Description,
        Count: link.Count,
        Price: link.UnitPrice ?? link.Item.DefaultPrice ?? 0,
        VatRate: link.Item.VatRate,
        PriceIncludesVat: link.PriceIncludesVat,
      }));

  return { grave, currentInvoiced, lines, invoices };
}
