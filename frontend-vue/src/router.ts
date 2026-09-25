import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";

/** The UI5 app's routes, same paths. */
const routes: RouteRecordRaw[] = [
  { path: "/", redirect: "/invoices" },
  { path: "/settings", component: () => import("@/pages/SettingsPage.vue") },
  // pages: add new routes above this line
];

export const router = createRouter({ history: createWebHistory(), routes });
