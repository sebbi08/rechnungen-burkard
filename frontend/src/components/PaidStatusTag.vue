<script setup lang="ts">
import Tag from "primevue/tag";
import { useI18n } from "vue-i18n";

defineProps<{ paidOn: string | null }>();
const { t } = useI18n();
</script>

<template>
  <!-- the date is recorded, never shown: set means paid, null unpaid. The role
       makes the longer label what a screen reader says: on a plain span several
       of them ignore an aria-label. -->
  <Tag
    :value="paidOn ? t('statusPaid') : t('statusUnpaid')"
    :severity="paidOn ? 'success' : 'warn'"
    role="img"
    :aria-label="paidOn ? t('statusPaidAnnouncement') : t('statusUnpaidAnnouncement')"
  />
</template>
