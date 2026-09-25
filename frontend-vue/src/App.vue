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
  <Toast position="top-right" />
  <ConfirmDialog />
  <MessageDialog />
  <BlockingProgress />
</template>
