import { computed, ref, type Ref, watch } from "vue";
import { type PriceIncreasePreview, previewItemPriceIncrease } from "@/api/endpoints";
import type { IncreaseRow } from "@/lib/priceIncrease";
import { useDebounced } from "./useDebounced";

export interface IncreaseInput {
  itemId: number;
  rows: IncreaseRow[];
}

/** Asks the server to check a price increase once its input has been still
 *  for 300 ms. The input is null while no ticked row differs from its old
 *  price. An answer to an older input is dropped; `answered` is the input the
 *  current preview belongs to, so its rows map to the right ones. */
export function useItemPriceIncreasePreview(input: Ref<IncreaseInput | null>) {
  const preview = ref<PriceIncreasePreview | null>(null);
  const answered = ref<IncreaseInput | null>(null);
  const pending = ref(false);
  const failed = ref(false);
  const key = computed(() => JSON.stringify(input.value));
  const debounced = useDebounced(key, 300);
  let latest = 0;

  async function run(raw: string): Promise<void> {
    const current = ++latest;
    const value = JSON.parse(raw) as IncreaseInput | null;
    if (value == null) {
      preview.value = null;
      answered.value = null;
      pending.value = false;
      failed.value = false;
      return;
    }
    pending.value = true;
    try {
      const answer = await previewItemPriceIncrease(value.itemId, value.rows);
      if (current === latest) {
        preview.value = answer;
        answered.value = value;
        failed.value = false;
      }
    } catch {
      if (current === latest) {
        failed.value = true;
        preview.value = null;
        answered.value = null;
      }
    } finally {
      if (current === latest) {
        pending.value = false;
      }
    }
  }

  watch(debounced, (raw) => void run(raw), { immediate: true });

  return { preview, answered, pending, failed, key };
}
