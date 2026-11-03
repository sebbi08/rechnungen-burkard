/// <reference types="vite/client" />

interface ImportMetaEnv {
  // PrimeUI license key for PrimeVue 5, passed in at build time; unset in
  // development and CI, where PrimeVue shows its license notice instead.
  readonly VITE_PRIMEUI_LICENSE?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
