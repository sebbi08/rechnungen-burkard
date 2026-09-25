import "primeicons/primeicons.css";
import "./main.css";
import Aura from "@primeuix/themes/aura";
import { QueryClient, VueQueryPlugin } from "@tanstack/vue-query";
import PrimeVue from "primevue/config";
import ConfirmationService from "primevue/confirmationservice";
import ToastService from "primevue/toastservice";
import Tooltip from "primevue/tooltip";
import { createApp } from "vue";
import App from "./App.vue";
import { applyStoredTheme } from "./composables/useTheme";
import { i18n, locale } from "./i18n";
import { router } from "./router";

applyStoredTheme();
document.documentElement.lang = locale;
document.title = i18n.global.t("appTitle");

// Data only changes through this app or the UI5 one, so a refetch on every
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
  })
  .use(ToastService)
  .use(ConfirmationService)
  .directive("tooltip", Tooltip)
  .use(VueQueryPlugin, { queryClient })
  .use(i18n)
  .use(router)
  .mount("#app");
