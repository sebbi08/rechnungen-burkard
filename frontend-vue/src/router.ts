import { createRouter, createWebHistory, type RouteRecordRaw } from "vue-router";

/** The UI5 app's routes, same paths. */
const routes: RouteRecordRaw[] = [{ path: "/", redirect: "/invoices" }];

export const router = createRouter({ history: createWebHistory(), routes });
