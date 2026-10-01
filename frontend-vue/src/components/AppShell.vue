<script setup lang="ts">
import Button from "primevue/button";
import Drawer from "primevue/drawer";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { useRoute } from "vue-router";
import { useTheme } from "@/composables/useTheme";
import NavLinks from "./NavLinks.vue";

const { t } = useI18n();
const route = useRoute();
const { dark, toggle } = useTheme();
const drawerOpen = ref(false);

watch(
  () => route.fullPath,
  () => {
    drawerOpen.value = false;
  },
);
</script>

<template>
  <div class="flex min-h-screen">
    <!-- from lg only: on a tablet the 16rem it takes left the tables too narrow
         for their own columns, which hide by viewport width -->
    <aside
      class="sticky top-0 hidden h-screen w-64 shrink-0 flex-col gap-6 border-r border-surface bg-surface-0 p-4 lg:flex dark:bg-surface-900"
    >
      <div class="flex items-center gap-2 px-3 pt-2 text-lg font-semibold">
        <i class="pi pi-file-edit text-primary" aria-hidden="true" />
        {{ t("appTitle") }}
      </div>
      <NavLinks />
      <Button
        class="mt-auto"
        :icon="dark ? 'pi pi-sun' : 'pi pi-moon'"
        :label="t('themeToggle')"
        severity="secondary"
        text
        @click="toggle"
      />
    </aside>

    <Drawer v-model:visible="drawerOpen" :header="t('appTitle')">
      <!-- a tap on the current page's link changes no route, so it closes here -->
      <NavLinks @click="drawerOpen = false" />
    </Drawer>

    <div class="flex min-w-0 flex-1 flex-col">
      <header
        class="sticky top-0 z-10 flex items-center gap-2 border-b border-surface bg-surface-0 px-3 py-2 lg:hidden dark:bg-surface-900"
      >
        <Button icon="pi pi-bars" text rounded :aria-label="t('menu')" @click="drawerOpen = true" />
        <span class="flex-1 font-semibold">{{ t("appTitle") }}</span>
        <Button
          :icon="dark ? 'pi pi-sun' : 'pi pi-moon'"
          text
          rounded
          :aria-label="t('themeToggle')"
          @click="toggle"
        />
      </header>
      <main class="mx-auto w-full max-w-7xl flex-1 p-4 md:p-8">
        <slot />
      </main>
    </div>
  </div>
</template>
