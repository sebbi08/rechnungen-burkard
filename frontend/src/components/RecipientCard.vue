<script setup lang="ts">
import { computed } from "vue";
import { formatRecipientLabel } from "@/lib/format";

const props = defineProps<{ number?: string | null; name: string; address: string }>();
const label = computed(() => formatRecipientLabel(props.number, props.name));
</script>

<template>
  <div class="flex min-w-0 flex-col text-sm [overflow-wrap:anywhere]" data-testid="recipient-card">
    <span class="font-medium">{{ label }}</span>
    <span class="whitespace-pre-line text-muted-color">{{ address }}</span>
  </div>
</template>
