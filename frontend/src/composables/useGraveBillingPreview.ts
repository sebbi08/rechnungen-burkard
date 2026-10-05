import { computed, ref, type Ref, watch } from "vue";
import { type BillingPreview, previewGraveBilling, type PreviewRequest } from "@/api/endpoints";
import { useDebounced } from "./useDebounced";

/** A preview request plus, per line, the editor row it came from. */
export interface PreviewInput extends PreviewRequest {
  keys: number[];
}

/** Asks for the grave's billing preview once the input has been still for
 *  300 ms. An answer to an older input is dropped, and the last answer stays
 *  on screen while the next one is on its way. `answered` is the input the
 *  current preview belongs to, so its line indexes map to the right rows even
 *  while the user keeps typing. */
export function useGraveBillingPreview(input: Ref<PreviewInput | null>) {
  const preview = ref<BillingPreview | null>(null);
  const answered = ref<PreviewInput | null>(null);
  const pending = ref(false);
  const failed = ref(false);
  const key = computed(() => JSON.stringify(input.value));
  const debounced = useDebounced(key, 300);
  let latest = 0;

  async function run(raw: string): Promise<BillingPreview | null> {
    const current = ++latest;
    const value = JSON.parse(raw) as PreviewInput | null;
    if (value == null) {
      preview.value = null;
      answered.value = null;
      pending.value = false;
      failed.value = false;
      return null;
    }
    pending.value = true;
    try {
      const answer = await previewGraveBilling(value);
      if (current === latest) {
        preview.value = answer;
        answered.value = value;
        failed.value = false;
      }
      return answer;
    } catch {
      if (current === latest) {
        failed.value = true;
      }
      return null;
    } finally {
      if (current === latest) {
        pending.value = false;
      }
    }
  }

  watch(debounced, (raw) => void run(raw), { immediate: true });

  /** Checks the current input now, without waiting for the debounce. */
  function check(): Promise<BillingPreview | null> {
    return run(key.value);
  }

  return { preview, answered, pending, failed, check };
}
