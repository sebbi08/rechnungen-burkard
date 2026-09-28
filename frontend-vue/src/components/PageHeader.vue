<script setup lang="ts">
import { RouterLink, type RouteLocationRaw } from "vue-router";

defineProps<{
  title: string;
  crumbs?: { label: string; to?: RouteLocationRaw }[];
}>();
</script>

<template>
  <div class="mb-6 flex flex-wrap items-end justify-between gap-3">
    <div class="min-w-0">
      <nav
        v-if="crumbs?.length"
        aria-label="Breadcrumb"
        class="mb-1 flex flex-wrap items-center gap-1 text-sm text-muted-color"
      >
        <template v-for="(crumb, index) in crumbs" :key="index">
          <RouterLink v-if="crumb.to" :to="crumb.to" class="text-muted-color hover:underline">
            {{ crumb.label }}
          </RouterLink>
          <span v-else>{{ crumb.label }}</span>
          <i
            v-if="index < crumbs.length - 1"
            class="pi pi-angle-right text-xs"
            aria-hidden="true"
          />
        </template>
      </nav>
      <h1 class="m-0 truncate text-2xl font-semibold">{{ title }}</h1>
    </div>
    <div class="flex flex-wrap items-center gap-2">
      <slot name="actions" />
    </div>
  </div>
</template>
