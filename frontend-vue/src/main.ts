import "primeicons/primeicons.css";
import "./main.css";
import Aura from "@primeuix/themes/aura";
import PrimeVue from "primevue/config";
import { createApp } from "vue";
import App from "./App.vue";

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
  .mount("#app");
