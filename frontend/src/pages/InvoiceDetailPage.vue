<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import { computed, ref, toRef, watch } from "vue";
import { useI18n } from "vue-i18n";
import { useRouter } from "vue-router";
import { loadChain } from "@/api/queries";
import InvoiceEditor from "@/components/InvoiceEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import RevisionList from "@/components/RevisionList.vue";
import { usePaidToggle } from "@/composables/usePaidToggle";
import { formatInvoiceNumber } from "@/lib/format";

const props = defineProps<{ rootId: number }>();
const { t } = useI18n();
const togglePaid = usePaidToggle();

const chain = useQuery({
  queryKey: ["invoices", "chain", toRef(props, "rootId")],
  queryFn: () => loadChain(props.rootId),
});
const latest = computed(() => chain.data.value?.[0]);

// A grave invoice is revised through its grave; here its editor would only be
// refused. A link or bookmark to one lands on its own page instead.
const router = useRouter();
watch(
  latest,
  (invoice) => {
    if (invoice?.GraveId != null) {
      void router.replace(`/graves/${invoice.GraveId}/invoices/${invoice.RootId}`);
    }
  },
  { immediate: true },
);
const number = computed(() => formatInvoiceNumber(latest.value?.InvoiceNumber));
const editorTarget = computed(() =>
  latest.value ? { invoiceId: latest.value.Id, recipientId: latest.value.RecipientId } : null,
);

const editorOpen = ref(false);
</script>

<template>
  <PageHeader
    :title="latest ? number : t('invoiceDetailTitle')"
    :crumbs="[
      { label: t('menuInvoices'), to: '/invoices' },
      { label: number || t('invoiceDetailTitle') },
    ]"
  >
    <template v-if="latest" #actions>
      <PaidStatusTag :paid-on="latest.PaidOn" />
      <Button
        :icon="latest.PaidOn ? 'pi pi-undo' : 'pi pi-wallet'"
        :label="latest.PaidOn ? t('actionMarkUnpaid') : t('actionMarkPaid')"
        severity="secondary"
        outlined
        @click="togglePaid(latest.Id, latest.PaidOn)"
      />
      <span v-tooltip.bottom="latest.PaidOn ? t('editBlockedPaid') : undefined">
        <Button
          :label="t('actionEdit')"
          icon="pi pi-pencil"
          :disabled="!!latest.PaidOn"
          @click="editorOpen = true"
        />
      </span>
    </template>
  </PageHeader>

  <div v-if="chain.isPending.value" class="flex justify-center p-10"><ProgressSpinner /></div>
  <Message v-else-if="!latest" severity="warn">{{ t("notFound") }}</Message>
  <div v-else class="flex flex-col gap-6">
    <Card>
      <template #content>
        <div class="text-lg font-semibold">{{ latest.RecipientName }}</div>
        <div class="whitespace-pre-line text-muted-color">{{ latest.RecipientAddress }}</div>
      </template>
    </Card>
    <RevisionList :chain="chain.data.value ?? []" />
  </div>

  <InvoiceEditor v-model:visible="editorOpen" :target="editorTarget" />
</template>
