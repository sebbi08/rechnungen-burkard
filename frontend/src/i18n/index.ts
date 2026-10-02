import { createI18n } from "vue-i18n";
import de from "./de.json";

/** The UI is German only; the catalogue keeps its texts out of the components. */
export const i18n = createI18n({
  legacy: false,
  locale: "de",
  messages: { de },
});
