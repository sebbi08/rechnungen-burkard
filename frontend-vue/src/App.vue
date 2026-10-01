<script setup lang="ts">
import { usePrimeVue } from "primevue/config";
import ConfirmDialog from "primevue/confirmdialog";
import Toast from "primevue/toast";
import { RouterView } from "vue-router";
import AppShell from "@/components/AppShell.vue";
import BlockingProgress from "@/components/BlockingProgress.vue";
import MessageDialog from "@/components/MessageDialog.vue";
import { locale } from "@/i18n";
import { primeLocaleDe } from "@/i18n/prime";

const primevue = usePrimeVue();
if (locale === "de" && primevue.config.locale) {
  Object.assign(primevue.config.locale, primeLocaleDe);
}
</script>

<template>
  <AppShell>
    <RouterView />
  </AppShell>
  <!-- 25rem wide by default, which pushed it off a phone's left edge -->
  <Toast
    position="top-right"
    :breakpoints="{ '640px': { width: 'calc(100vw - 2rem)', right: '1rem' } }"
  />
  <ConfirmDialog />
  <MessageDialog />
  <BlockingProgress />
</template>
