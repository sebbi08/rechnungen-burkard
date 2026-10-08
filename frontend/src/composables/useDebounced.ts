import { onScopeDispose, ref, type Ref, watch } from "vue";

/** `source`, but only once it has stopped changing for `ms`. */
export function useDebounced<T>(source: Ref<T>, ms = 300): Ref<T> {
  const value = ref(source.value) as Ref<T>;
  let timer: ReturnType<typeof setTimeout> | undefined;
  watch(source, (next) => {
    clearTimeout(timer);
    timer = setTimeout(() => {
      value.value = next;
    }, ms);
  });
  onScopeDispose(() => clearTimeout(timer));
  return value;
}
