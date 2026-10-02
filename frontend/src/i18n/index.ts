import { createI18n } from "vue-i18n";
import de from "./de.json";
import en from "./en.json";

/** German unless the browser prefers English.
 *  Numbers and dates stay de-DE either way. */
export const locale: "de" | "en" = navigator.language.toLowerCase().startsWith("en") ? "en" : "de";

export const i18n = createI18n({
  legacy: false,
  locale,
  fallbackLocale: "de",
  messages: { de, en },
});
