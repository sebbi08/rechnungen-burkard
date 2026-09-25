import { type QueryClient, useQuery } from "@tanstack/vue-query";
import type { Ref } from "vue";
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

/** Items, newest first; a term filters on number or description. */
export function fetchItems(term: string): Promise<InvoiceItem[]> {
  const trimmed = term.trim();
  return readAll(async (skip, top) => {
    const response = await odata
      .InvoiceItems()
      .query((b, q) => {
        if (trimmed) {
          b.filter(q.Number.contains(trimmed).or(q.Description.contains(trimmed)));
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

/** Recipients, newest first; a term filters on the name. */
export function fetchRecipients(term: string): Promise<Recipient[]> {
  const trimmed = term.trim();
  return readAll(async (skip, top) => {
    const response = await odata
      .Recipients()
      .query((b, q) => {
        if (trimmed) {
          b.filter(q.Name.contains(trimmed));
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
