import { keepPreviousData, type QueryClient, useQuery } from "@tanstack/vue-query";
import type { Ref } from "vue";
import { currentHalfYear } from "@/lib/halfYear";
import { priceFieldValue } from "@/lib/money";
import { PAGE_SIZE } from "@/lib/paging";
import { fetchGraveDetails, fetchGraveInvoiceGroup, type GraveDetails } from "./endpoints";
import type { Invoice, InvoiceItem } from "./generated";
import { odata } from "./odata";

/** The backend caps $top at 200 (Program.cs, SetMaxTop). */
const READ_ALL_CHUNK = 200;

/** Reads a whole collection in pages the server accepts. */
export async function readAll<T>(page: (skip: number, top: number) => Promise<T[]>): Promise<T[]> {
  const rows: T[] = [];
  for (let skip = 0; ; skip += READ_ALL_CHUNK) {
    const chunk = await page(skip, READ_ALL_CHUNK);
    rows.push(...chunk);
    if (chunk.length < READ_ALL_CHUNK) {
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

/** How many recipients a picker offers at a time. */
export const RECIPIENT_PICKER_SIZE = 20;

/** What a recipient picker offers: the newest recipients a term leaves (in the
 *  name, the number or the address), without the ones already taken, and how
 *  many it leaves in all. Never the whole list: there are thousands. */
export function useRecipientPickerQuery(term: Ref<string>, exclude: Ref<readonly number[]>) {
  return useQuery({
    queryKey: ["recipients", "picker", term, exclude],
    queryFn: async () => {
      const literal = searchLiteral(term.value);
      const response = await odata
        .Recipients()
        .query((b, q) => {
          // left out by the server, so twenty others come in their place
          b.filter(...exclude.value.map((id) => q.Id.ne(id)));
          if (literal) {
            // grouped: "and" binds tighter than "or", and without the brackets
            // a recipient left out above came back through its number or address
            b.filter(
              q.Name.toLower()
                .contains(literal)
                .or(q.Number.toLower().contains(literal))
                .or(q.Address.toLower().contains(literal))
                .group(),
            );
          }
          b.orderBy(q.Id.desc()).top(RECIPIENT_PICKER_SIZE).count();
        })
        .execute();
      return { rows: response.data.value, total: response.data["@odata.count"] ?? 0 };
    },
    placeholderData: keepPreviousData,
  });
}

/** One recipient, or nothing for no id: a picker's pick that is not among the
 *  recipients it offers. */
export function useRecipientQuery(id: Ref<number | null>) {
  return useQuery({
    queryKey: ["recipients", "one", id],
    queryFn: async () => (await odata.Recipients(id.value!).query().execute()).data,
    enabled: () => id.value != null,
  });
}

/** One page of the recipients a term leaves, newest first, and how many it
 *  leaves in all. `first` is the index of the page's first row. */
export function useRecipientsPageQuery(term: Ref<string>, first: Ref<number>) {
  return useQuery({
    queryKey: ["recipients", "page", term, first],
    queryFn: async () => {
      const literal = searchLiteral(term.value);
      const response = await odata
        .Recipients()
        .query((b, q) => {
          if (literal) {
            b.filter(q.Name.toLower().contains(literal).or(q.Number.toLower().contains(literal)));
          }
          b.orderBy(q.Id.desc()).skip(first.value).top(PAGE_SIZE).count();
        })
        .execute();
      return { rows: response.data.value, total: response.data["@odata.count"] ?? 0 };
    },
    placeholderData: keepPreviousData,
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

export interface GraveYearRange {
  first: number;
  last: number;
}

/** The years the overview's year field spans: from the earliest grave start to
 *  the latest grave end, and always up to next year, since a grave without an
 *  end runs into the future. Without any grave: two years back to next year. */
export function useGraveYearRange() {
  return useQuery({
    queryKey: ["graves", "yearRange"],
    queryFn: async (): Promise<GraveYearRange> => {
      const thisYear = new Date().getFullYear();
      const [earliest, latest] = await Promise.all([
        odata
          .Graves()
          .query((b, q) => {
            b.select("StartDate").orderBy(q.StartDate.asc()).top(1);
          })
          .execute(),
        odata
          .Graves()
          .query((b, q) => {
            b.select("EndDate").filter(q.EndDate.ne(null)).orderBy(q.EndDate.desc()).top(1);
          })
          .execute(),
      ]);
      const start = earliest.data.value[0]?.StartDate;
      const end = latest.data.value[0]?.EndDate;
      return {
        first: start ? Number(start.slice(0, 4)) : thisYear - 2,
        last: Math.max(end ? Number(end.slice(0, 4)) : 0, thisYear + 1),
      };
    },
  });
}
