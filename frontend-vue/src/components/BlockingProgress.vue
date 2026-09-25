<script setup lang="ts">
import ProgressBar from "primevue/progressbar";
import { blockingState } from "@/composables/useBlocking";
</script>

<template>
  <!-- above PrimeVue's overlays (dialogs and drawers stack from 1100) -->
  <div
    v-if="blockingState.active"
    class="fixed inset-0 z-[5000] flex items-center justify-center bg-black/40 p-4"
    role="alertdialog"
    aria-modal="true"
    aria-busy="true"
    :aria-label="blockingState.title"
  >
    <div class="w-full max-w-sm rounded-xl bg-surface-0 p-6 shadow-xl dark:bg-surface-900">
      <p class="mt-0 mb-4 font-medium">{{ blockingState.title }}</p>
      <ProgressBar v-if="blockingState.percent !== null" :value="blockingState.percent" />
      <ProgressBar v-else mode="indeterminate" style="height: 6px" />
    </div>
  </div>
</template>
