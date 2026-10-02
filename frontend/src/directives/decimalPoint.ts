import type { Directive } from "vue";

/** Lets a typed "." mean the decimal comma in a de-DE InputNumber. PrimeVue
 *  accepts only the locale's own decimal sign there and drops anything else, so
 *  "12.5" became 125,00 without a word. The key is replaced before PrimeVue's
 *  own keypress handler sees it. */
function onKeyPress(event: KeyboardEvent): void {
  if (event.key !== "." || !(event.target instanceof HTMLInputElement)) {
    return;
  }
  event.preventDefault();
  event.stopImmediatePropagation();
  event.target.dispatchEvent(
    new KeyboardEvent("keypress", { key: ",", bubbles: true, cancelable: true }),
  );
}

export const vDecimalPoint: Directive<HTMLElement> = {
  mounted(el) {
    // capture: the root element sees the key before the input's own listener
    el.addEventListener("keypress", onKeyPress, true);
  },
  unmounted(el) {
    el.removeEventListener("keypress", onKeyPress, true);
  },
};
