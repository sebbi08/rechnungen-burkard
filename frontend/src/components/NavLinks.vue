<script setup lang="ts">
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink, useRoute } from "vue-router";

const { t } = useI18n();
const route = useRoute();

const links = computed(() => [
  { to: "/invoices", label: t("menuInvoices"), icon: "pi pi-file" },
  { to: "/quotes", label: t("menuQuotes"), icon: "pi pi-file-edit" },
  { to: "/graves", label: t("menuGraves"), icon: "pi pi-home" },
  { to: "/cooperative-graves", label: t("menuCooperativeGraves"), icon: "pi pi-home" },
  { to: "/inactive-graves", label: t("menuInactiveGraves"), icon: "pi pi-history" },
  { to: "/lists", label: t("menuLists"), icon: "pi pi-list" },
  { to: "/items", label: t("menuItems"), icon: "pi pi-box" },
  { to: "/recipients", label: t("menuRecipients"), icon: "pi pi-users" },
  { to: "/import-export", label: t("menuImportExport"), icon: "pi pi-upload" },
  { to: "/settings", label: t("menuSettings"), icon: "pi pi-cog" },
]);

// detail routes are siblings, not children, so RouterLink's own active class
// would not light up "Gräber" on /graves/12
function isActive(to: string): boolean {
  return route.path === to || route.path.startsWith(`${to}/`);
}
</script>

<template>
  <nav :aria-label="t('navigation')" class="flex flex-col gap-1">
    <RouterLink
      v-for="link in links"
      :key="link.to"
      :to="link.to"
      :aria-current="isActive(link.to) ? 'page' : undefined"
      class="flex items-center gap-3 rounded-lg px-3 py-2 text-color no-underline transition-colors hover:bg-emphasis"
      :class="{ 'bg-emphasis font-semibold text-primary': isActive(link.to) }"
    >
      <i :class="link.icon" aria-hidden="true" />
      <span>{{ link.label }}</span>
    </RouterLink>
  </nav>
</template>
