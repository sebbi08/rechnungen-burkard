import { computed, type ComputedRef, reactive, type Ref, watch } from "vue";
import { type LocationQuery, type LocationQueryRaw, useRoute, useRouter } from "vue-router";
import { useDebounced } from "./useDebounced";

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
  /** `values` once they have stopped changing for 300 ms: what the queries use. */
  readonly applied: Readonly<Ref<Record<K, string>>>;
  /** How many keys are set to something other than their default; a value of
   *  only whitespace filters nothing and counts as its default. */
  readonly active: ComputedRef<number>;
  /** Sets `key` -- or, without one, every key -- back to its default. */
  clear(key?: K): void;
}

/** A list's filters, kept in the page's URL query so that coming back from a
 *  detail page, reloading or sharing the link shows the same filtered list.
 *  `defaults` names every key and the value that means "no filter"; a key at
 *  its default is left out of the URL. Keys the URL holds that are not in
 *  `defaults` are left alone, and a key it lacks starts at its default.
 *
 *  Every change replaces the URL rather than pushing it, so the browser's back
 *  button still leaves the list instead of undoing keystrokes. A navigation to
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

  const state = reactive(fromQuery(route.query)) as Record<K, string>;

  // the URL change this sets off comes back through the watch below, which
  // must not take it for a navigation and set the fields back to it while the
  // user has typed on
  let writing = 0;
  watch(
    () => ({ ...state }),
    (next) => {
      if (route.path !== path) {
        return;
      }
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
      writing++;
      void router.replace({ query }).finally(() => {
        writing--;
      });
    },
  );

  watch(
    () => route.query,
    (query) => {
      if (writing > 0 || route.path !== path) {
        return;
      }
      const read = fromQuery(query);
      for (const key of keys) {
        if (state[key] !== read[key]) {
          state[key] = read[key];
        }
      }
    },
  );

  const applied = useDebounced(computed(() => ({ ...state })));

  const active = computed(
    () => keys.filter((key) => state[key].trim() !== defaults[key].trim()).length,
  );

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
    active,
    clear,
  };
}
