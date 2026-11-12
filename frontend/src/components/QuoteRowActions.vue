<script setup lang="ts">
import Button from "primevue/button";
import Menu from "primevue/menu";
import type { MenuItem } from "primevue/menuitem";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { type RouteLocationRaw, useRouter } from "vue-router";
import { quotePdfUrl } from "@/api/endpoints";
import { QuoteState } from "@/api/generated";
import { useQuoteActions } from "@/composables/useQuoteActions";

const props = defineProps<{
  /** The latest version: the one downloaded, and the one the actions address. */
  quoteId: number;
  state: QuoteState;
  detailsTo: RouteLocationRaw;
  /** Which row, for screen readers: every row has the same buttons. */
  rowLabel?: string;
}>();
const emit = defineEmits<{ edit: [] }>();

const { t } = useI18n();
const router = useRouter();
const actions = useQuoteActions();
const menu = ref<InstanceType<typeof Menu>>();

function named(label: string): string {
  return props.rowLabel ? `${label}: ${props.rowLabel}` : label;
}

function download(): void {
  // opened inside the click, or the browser blocks it as a popup
  window.open(quotePdfUrl(props.quoteId), "_blank");
}

interface Action {
  key: string;
  label: string;
  icon: string;
  severity?: "secondary" | "success" | "danger";
  run: () => void;
}

// what the state allows: an open quote everything, a declined one reopening
// and deleting, a converted one looking only
const available = computed<Action[]>(() => {
  const label = props.rowLabel ?? "";
  const list: Action[] = [
    {
      key: "details",
      label: t("actionDetails"),
      icon: "pi pi-eye",
      run: () => void router.push(props.detailsTo),
    },
    { key: "download", label: t("actionDownloadQuote"), icon: "pi pi-file-pdf", run: download },
  ];
  if (props.state === QuoteState.Open) {
    list.push(
      { key: "edit", label: t("actionEdit"), icon: "pi pi-pencil", run: () => emit("edit") },
      {
        key: "convert",
        label: t("actionConvertQuote"),
        icon: "pi pi-file-export",
        severity: "success",
        run: () => void actions.convert(props.quoteId, label),
      },
      {
        key: "decline",
        label: t("actionDeclineQuote"),
        icon: "pi pi-times-circle",
        severity: "secondary",
        run: () => void actions.decline(props.quoteId),
      },
    );
  } else if (props.state === QuoteState.Declined) {
    list.push({
      key: "reopen",
      label: t("actionReopenQuote"),
      icon: "pi pi-undo",
      severity: "secondary",
      run: () => void actions.reopen(props.quoteId),
    });
  }
  if (props.state !== QuoteState.Converted) {
    list.push({
      key: "delete",
      label: t("actionDeleteQuote"),
      icon: "pi pi-trash",
      severity: "danger",
      run: () => void actions.remove(props.quoteId, label),
    });
  }
  return list;
});

const menuItems = computed<MenuItem[]>(() =>
  available.value.map((action) => ({
    label: action.label,
    icon: action.icon,
    command: action.run,
  })),
);
</script>

<template>
  <div class="flex items-center justify-end gap-0.5">
    <!-- xl, not md, as on the invoice rows: below it the buttons push the
         row's own columns out of the table -->
    <div class="hidden items-center gap-0.5 xl:flex">
      <Button
        v-for="action in available"
        :key="action.key"
        v-tooltip.top="action.label"
        :icon="action.icon"
        text
        rounded
        :severity="action.severity"
        :aria-label="named(action.label)"
        @click="action.run"
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
