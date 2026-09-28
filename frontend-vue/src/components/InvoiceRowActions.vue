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
}>();
const emit = defineEmits<{ edit: [] }>();

const { t } = useI18n();
const router = useRouter();
const { showError } = useErrorToast();
const togglePaid = usePaidToggle();
const menu = ref<InstanceType<typeof Menu>>();

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
    <div class="hidden items-center gap-0.5 md:flex">
      <Button
        v-tooltip.top="t('actionDetails')"
        icon="pi pi-eye"
        text
        rounded
        :aria-label="t('actionDetails')"
        @click="router.push(detailsTo)"
      />
      <Button
        v-tooltip.top="t('actionDownloadNormal')"
        icon="pi pi-file-pdf"
        text
        rounded
        :aria-label="t('actionDownloadNormal')"
        @click="open('Normal')"
      />
      <Button
        v-tooltip.top="t('actionDownloadCopy')"
        icon="pi pi-copy"
        text
        rounded
        :aria-label="t('actionDownloadCopy')"
        @click="open('Copy')"
      />
      <!-- a disabled button fires no hover, so the tooltip sits on a wrapper -->
      <span v-tooltip.top="paidOn ? t('editBlockedPaid') : t('actionEdit')">
        <Button
          icon="pi pi-pencil"
          text
          rounded
          :disabled="!!paidOn"
          :aria-label="t('actionEdit')"
          @click="emit('edit')"
        />
      </span>
      <Button
        v-tooltip.top="paidOn ? t('actionMarkUnpaid') : t('actionMarkPaid')"
        :icon="paidOn ? 'pi pi-undo' : 'pi pi-wallet'"
        text
        rounded
        :severity="paidOn ? 'secondary' : 'success'"
        :aria-label="paidOn ? t('actionMarkUnpaid') : t('actionMarkPaid')"
        @click="togglePaid(invoiceId, paidOn)"
      />
    </div>
    <Button
      class="md:hidden"
      icon="pi pi-ellipsis-v"
      text
      rounded
      :aria-label="t('moreActions')"
      aria-haspopup="true"
      @click="menu?.toggle($event)"
    />
    <Menu ref="menu" :model="menuItems" popup />
  </div>
</template>
