import { onScopeDispose, reactive, ref, type Ref, watch } from "vue";
import {
  type LocationQuery,
  type LocationQueryRaw,
  onBeforeRouteLeave,
  type RouteLocationRaw,
  useRoute,
  useRouter,
} from "vue-router";

/** One searchable column of a list, as its filter row cell and the phone's
 *  filter sheet show it. A select's options need not hold the empty value:
 *  "Alle" is added in front of them. */
export interface ColumnFilterField {
  key: string;
  header: string;
  kind: "text" | "select";
  options?: { value: string; label: string }[];
}

export interface ColumnFilters<K extends string> {
  /** What the controls bind, one string per key. Assigning a whole object (a
   *  `v-model:values` on the filter sheet) copies its keys in. */
  values: Record<K, string>;
  /** `values`, trimmed, once they have stopped changing for 300 ms: what the
   *  queries use. It changes only when a filter does, so whitespace typed
   *  around a term neither fetches again nor goes back to page one. */
  readonly applied: Readonly<Ref<Record<K, string>>>;
  /** Sets `key` -- or, without one, every key -- back to its default. */
  clear(key?: K): void;
}

/** How long `values` must stay unchanged before they apply. */
const SETTLE_MS = 300;

/** The query each list's URL had when it was last shown, by the list's path:
 *  what a breadcrumb back to it carries. */
const lastQueries = new Map<string, LocationQueryRaw>();

/** Where a link back to the list at `path` leads: the list as it was left,
 *  with its filters -- and on the grave overview its half-year -- or the bare
 *  list when it has not been shown since the app was loaded. For breadcrumbs;
 *  the menu leads to the bare list. */
export function listLocation(path: string): RouteLocationRaw {
  const query = lastQueries.get(path);
  return query === undefined ? path : { path, query: { ...query } };
}

/** A list's filters, kept in the page's URL query so that coming back from a
 *  detail page, reloading or sharing the link shows the same filtered list.
 *  `defaults` names every key and the value that means "no filter"; a key at
 *  its default is left out of the URL. Keys the URL holds that are not in
 *  `defaults` are left alone, and a key it lacks starts at its default.
 *
 *  The URL follows `applied`, not every keystroke: browsers limit how often a
 *  page may replace its URL, and past the limit the router falls back to
 *  reloading the page. Leaving the list writes what is still pending at once.
 *  Every change replaces the URL rather than pushing it, so the browser's back
 *  button still leaves the list instead of undoing searches. A navigation to
 *  the same list with another query (the menu link, back and forward) sets the
 *  filters to what it carries. */
export function useColumnFilters<K extends string>(defaults: Record<K, string>): ColumnFilters<K> {
  const route = useRoute();
  const router = useRouter();
  const keys = Object.keys(defaults) as K[];
  // `route` follows the app to the next page before this one is unmounted, so
  // what it says about another path is none of these filters' business
  const path = route.path;

  function fromQuery(query: LocationQuery): Record<K, string> {
    const read = {} as Record<K, string>;
    for (const key of keys) {
      const raw = query[key];
      const value = Array.isArray(raw) ? raw[0] : raw;
      read[key] = typeof value === "string" ? value : defaults[key];
    }
    return read;
  }

  function trimmed(values: Record<K, string>): Record<K, string> {
    const result = {} as Record<K, string>;
    for (const key of keys) {
      result[key] = values[key].trim();
    }
    return result;
  }

  function same(a: Record<K, string>, b: Record<K, string>): boolean {
    return keys.every((key) => a[key] === b[key]);
  }

  /** The list's URL query for `next`: the keys of other owners as they are. */
  function queryFor(next: Record<K, string>): LocationQueryRaw {
    const query: LocationQueryRaw = {};
    for (const [key, value] of Object.entries(route.query)) {
      if (!keys.includes(key as K)) {
        query[key] = value;
      }
    }
    for (const key of keys) {
      if (next[key] !== defaults[key]) {
        query[key] = next[key];
      }
    }
    return query;
  }

  const state = reactive(fromQuery(route.query)) as Record<K, string>;
  const applied = ref(trimmed(state)) as Ref<Record<K, string>>;
  lastQueries.set(path, { ...route.query });

  // the URL change a write sets off comes back through the watch on
  // `route.query`, which must not take it for a navigation and set the fields
  // back to it while the user has typed on
  let writing = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;

  /** Takes what the fields hold as the filters, and into the URL. */
  function apply(): void {
    clearTimeout(timer);
    const next = trimmed(state);
    if (same(next, applied.value) || route.path !== path) {
      return;
    }
    applied.value = next;
    writing++;
    void router.replace({ query: queryFor(next) }).finally(() => {
      writing--;
    });
  }

  watch(
    () => trimmed(state),
    () => {
      clearTimeout(timer);
      timer = setTimeout(apply, SETTLE_MS);
    },
  );

  watch(
    () => route.query,
    (query) => {
      if (route.path !== path) {
        return;
      }
      lastQueries.set(path, { ...query });
      if (writing > 0) {
        return;
      }
      const read = fromQuery(query);
      for (const key of keys) {
        if (state[key].trim() !== read[key].trim()) {
          state[key] = read[key];
        }
      }
      // a navigation's filters apply at once
      clearTimeout(timer);
      const next = trimmed(state);
      if (!same(next, applied.value)) {
        applied.value = next;
      }
    },
  );

  // A click into a detail page can come within the 300 ms, or cancel the
  // write those 300 ms set off: what was typed last goes into the list's URL
  // before it is left, or coming back would miss it. A router navigation here
  // would cancel the one that leaves, so the browser's entry for the list is
  // replaced underneath the router, which takes its URL from there when it
  // pushes the next page. Back and forward have moved the browser on already,
  // and leave the entry as it is.
  onBeforeRouteLeave((_to, from) => {
    clearTimeout(timer);
    const next = trimmed(state);
    if (!same(next, applied.value)) {
      applied.value = next;
    }
    const target = router.resolve({ path, query: queryFor(next) });
    lastQueries.set(path, { ...target.query });
    const history = router.options.history;
    const shown = router.resolve(history.location).fullPath;
    if (shown === from.fullPath && shown !== target.fullPath) {
      history.replace(target.fullPath);
    }
  });

  onScopeDispose(() => clearTimeout(timer));

  function clear(key?: K): void {
    for (const each of key === undefined ? keys : [key]) {
      state[each] = defaults[each];
    }
  }

  return {
    get values() {
      return state;
    },
    set values(next: Record<K, string>) {
      for (const key of keys) {
        if (typeof next[key] === "string") {
          state[key] = next[key];
        }
      }
    },
    applied,
    clear,
  };
}
