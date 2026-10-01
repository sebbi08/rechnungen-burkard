import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";

const routes: RouteRecordRaw[] = [
  { path: "/", redirect: "/invoices" },
  {
    path: "/settings",
    meta: { titleKey: "menuSettings" },
    component: () => import("@/pages/SettingsPage.vue"),
  },
  {
    path: "/items",
    meta: { titleKey: "menuItems" },
    component: () => import("@/pages/ItemsPage.vue"),
  },
  {
    path: "/recipients",
    meta: { titleKey: "menuRecipients" },
    component: () => import("@/pages/RecipientsPage.vue"),
  },
  {
    path: "/invoices",
    meta: { titleKey: "menuInvoices" },
    component: () => import("@/pages/InvoicesPage.vue"),
  },
  {
    path: "/invoices/:rootId",
    meta: { titleKey: "invoiceDetailTitle" },
    component: () => import("@/pages/InvoiceDetailPage.vue"),
    props: (route) => ({ rootId: Number(route.params.rootId) }),
  },
  {
    path: "/graves",
    meta: { titleKey: "menuGraves" },
    component: () => import("@/pages/GravesPage.vue"),
  },
  {
    path: "/graves/:graveId",
    meta: { titleKey: "graveDetailTitle" },
    component: () => import("@/pages/GraveDetailPage.vue"),
    props: (route) => ({ graveId: Number(route.params.graveId) }),
  },
  {
    path: "/graves/:graveId/invoices/:rootId",
    meta: { titleKey: "graveInvoiceDetailTitle" },
    component: () => import("@/pages/GraveInvoiceDetailPage.vue"),
    props: (route) => ({
      graveId: Number(route.params.graveId),
      rootId: Number(route.params.rootId),
    }),
  },
  {
    path: "/import-export",
    meta: { titleKey: "importExportTitle" },
    component: () => import("@/pages/ImportExportPage.vue"),
  },
  // pages: add new routes above this line
  { path: "/:pathMatch(.*)*", redirect: "/invoices" },
];

export const router = createRouter({ history: createWebHistory(), routes });

declare module "vue-router" {
  interface RouteMeta {
    /** The page's name for the browser tab and history, as an i18n key. */
    titleKey?: string;
  }
}
