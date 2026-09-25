import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";

/** The UI5 app's routes, same paths. */
const routes: RouteRecordRaw[] = [
  { path: "/", redirect: "/invoices" },
  { path: "/settings", component: () => import("@/pages/SettingsPage.vue") },
  { path: "/items", component: () => import("@/pages/ItemsPage.vue") },
  { path: "/recipients", component: () => import("@/pages/RecipientsPage.vue") },
  { path: "/invoices", component: () => import("@/pages/InvoicesPage.vue") },
  // pages: add new routes above this line
  { path: "/:pathMatch(.*)*", redirect: "/invoices" },
];

export const router = createRouter({ history: createWebHistory(), routes });
