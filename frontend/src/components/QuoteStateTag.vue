<script setup lang="ts">
import Tag from "primevue/tag";
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { QuoteState } from "@/api/generated";

const props = defineProps<{ state: QuoteState }>();
const { t } = useI18n();

const look = computed(() => {
  switch (props.state) {
    case QuoteState.Declined:
      return { label: t("quoteStateDeclined"), severity: "secondary" };
    case QuoteState.Converted:
      return { label: t("quoteStateConverted"), severity: "success" };
    default:
      return { label: t("quoteStateOpen"), severity: "info" };
  }
});
</script>

<template>
  <Tag :value="look.label" :severity="look.severity" />
</template>
