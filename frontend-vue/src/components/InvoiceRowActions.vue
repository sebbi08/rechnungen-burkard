<script setup lang="ts">
import Button from "primevue/button";
import Menu from "primevue/menu";
import type { MenuItem } from "primevue/menuitem";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { type RouteLocationRaw, useRouter } from "vue-router";
import type { DocumentKindName } from "@/api/endpoints";
import { useErrorToast } from "@/composables/useErrorToast";
import { usePaidToggle } from "@/composables/usePaidToggle";
import { openDocument } from "@/lib/download";

const props = defineProps<{
  invoiceId: number;
  paidOn: string | null;
  detailsTo: RouteLocationRaw;
  /** Which row, for screen readers: every row has the same buttons. */
  rowLabel?: string;
}>();
const emit = defineEmits<{ edit: [] }>();

const { t } = useI18n();
const router = useRouter();
const { showError } = useErrorToast();
const togglePaid = usePaidToggle();
const menu = ref<InstanceType<typeof Menu>>();

function named(label: string): string {
  return props.rowLabel ? `${label}: ${props.rowLabel}` : label;
}

function open(kind: DocumentKindName): void {
  // openDocument opens the tab synchronously, inside the click
  openDocument(props.invoiceId, kind).catch(showError);
}

const menuItems = computed<MenuItem[]>(() => [
  {
    label: t("actionDetails"),
    icon: "pi pi-eye",
    command: () => void router.push(props.detailsTo),
  },
  { label: t("actionDownloadNormal"), icon: "pi pi-file-pdf", command: () => open("Normal") },
  { label: t("actionDownloadCopy"), icon: "pi pi-copy", command: () => open("Copy") },
  {
    label: props.paidOn ? t("editBlockedPaid") : t("actionEdit"),
    icon: "pi pi-pencil",
    disabled: !!props.paidOn,
    command: () => emit("edit"),
  },
  {
    label: props.paidOn ? t("actionMarkUnpaid") : t("actionMarkPaid"),
    icon: props.paidOn ? "pi pi-undo" : "pi pi-wallet",
    command: () => void togglePaid(props.invoiceId, props.paidOn),
  },
]);
</script>

<template>
  <div class="flex items-center justify-end gap-0.5">
    <!-- xl, not md: the sidebar appears at md and takes 16rem, so below xl five
         buttons push the row's own columns out of the table -->
    <div class="hidden items-center gap-0.5 xl:flex">
      <Button
        v-tooltip.top="t('actionDetails')"
        icon="pi pi-eye"
        text
        rounded
        :aria-label="named(t('actionDetails'))"
        @click="router.push(detailsTo)"
      />
      <Button
        v-tooltip.top="t('actionDownloadNormal')"
        icon="pi pi-file-pdf"
        text
        rounded
        :aria-label="named(t('actionDownloadNormal'))"
        @click="open('Normal')"
      />
      <Button
        v-tooltip.top="t('actionDownloadCopy')"
        icon="pi pi-copy"
        text
        rounded
        :aria-label="named(t('actionDownloadCopy'))"
        @click="open('Copy')"
      />
      <!-- a disabled button fires no hover, so the tooltip sits on a wrapper -->
      <span v-tooltip.top="paidOn ? t('editBlockedPaid') : t('actionEdit')">
        <Button
          icon="pi pi-pencil"
          text
          rounded
          :disabled="!!paidOn"
          :aria-label="named(t('actionEdit'))"
          @click="emit('edit')"
        />
      </span>
      <Button
        v-tooltip.top="paidOn ? t('actionMarkUnpaid') : t('actionMarkPaid')"
        :icon="paidOn ? 'pi pi-undo' : 'pi pi-wallet'"
        text
        rounded
        :severity="paidOn ? 'secondary' : 'success'"
        :aria-label="named(paidOn ? t('actionMarkUnpaid') : t('actionMarkPaid'))"
        @click="togglePaid(invoiceId, paidOn)"
      />
    </div>
    <Button
      class="xl:hidden"
      icon="pi pi-ellipsis-v"
      text
      rounded
      :aria-label="named(t('moreActions'))"
      aria-haspopup="true"
      @click="menu?.toggle($event)"
    />
    <Menu ref="menu" :model="menuItems" popup />
  </div>
</template>
