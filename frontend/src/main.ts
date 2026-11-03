import "primeicons/primeicons.css";
import "./main.css";
// The compat variant of Aura: @primeuix/themes 3 made Aura's default sizes
// smaller (14px text, tighter padding); the compat variant keeps the sizes the
// layout was built with.
import Aura from "@primeuix/themes/aura-compat";
import { QueryClient, VueQueryPlugin } from "@tanstack/vue-query";
import PrimeVue from "primevue/config";
import ConfirmationService from "primevue/confirmationservice";
import ToastService from "primevue/toastservice";
import Tooltip from "primevue/tooltip";
import { createApp } from "vue";
import App from "./App.vue";
import { applyStoredTheme } from "./composables/useTheme";
import { i18n } from "./i18n";
import { router } from "./router";

applyStoredTheme();
document.title = i18n.global.t("appTitle");
// "Gräber – Rechnungen": every tab and history entry read just "Rechnungen"
router.afterEach((to) => {
  const app = i18n.global.t("appTitle");
  const page = to.meta.titleKey ? i18n.global.t(to.meta.titleKey) : "";
  document.title = page && page !== app ? `${page} – ${app}` : app;
});

// Data only changes through this app, so a refetch on every
// window focus would reload lists under an open editor for nothing. Instead
// every successful write invalidates all queries.
const queryClient = new QueryClient({
  defaultOptions: { queries: { refetchOnWindowFocus: false, retry: false } },
});

createApp(App)
  .use(PrimeVue, {
    theme: {
      preset: Aura,
      options: {
        darkModeSelector: ".app-dark",
        cssLayer: { name: "primevue", order: "theme, base, primevue" },
      },
    },
    // PrimeUI license key, set at build time (Docker build arg PRIMEUI_LICENSE).
    // Without one PrimeVue still works but shows an "Invalid PrimeUI License"
    // notice and logs a console warning. Never commit a key.
    license: import.meta.env.VITE_PRIMEUI_LICENSE || undefined,
  })
  .use(ToastService)
  .use(ConfirmationService)
  .directive("tooltip", Tooltip)
  .use(VueQueryPlugin, { queryClient })
  .use(i18n)
  .use(router)
  .mount("#app");
