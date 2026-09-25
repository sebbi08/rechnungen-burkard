import { fileURLToPath, URL } from "node:url";
import tailwindcss from "@tailwindcss/vite";
import vue from "@vitejs/plugin-vue";
import { defineConfig } from "vite";

// Where /odata is proxied to. Inside docker compose that is the backend
// container. For a click-through from the host, point it at the E2E stack
// (VITE_PROXY_TARGET=http://localhost:8081) -- never at the dev stack, whose
// database holds the user's data (see CLAUDE.md).
const proxyTarget = process.env.VITE_PROXY_TARGET ?? "http://backend:8080";

export default defineConfig({
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) },
  },
  define: {
    // vue-i18n's compile-time flags; unset they log a warning on every start
    __VUE_I18N_FULL_INSTALL__: true,
    __VUE_I18N_LEGACY_API__: false,
    __INTLIFY_PROD_DEVTOOLS__: false,
  },
  server: {
    port: 8082,
    strictPort: true,
    proxy: {
      "/odata": { target: proxyTarget, changeOrigin: true },
    },
  },
});
