<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import { computed, ref, toRef, watch } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink, useRouter } from "vue-router";
import { isNotFound } from "@/api/http";
import { loadChain, useQuoteQuery } from "@/api/queries";
import InvoiceEditor from "@/components/InvoiceEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import RevisionList from "@/components/RevisionList.vue";
import { listLocation } from "@/composables/useColumnFilters";
import { usePaidToggle } from "@/composables/usePaidToggle";
import { formatInvoiceNumber } from "@/lib/format";
import { buildRevisions } from "@/lib/revisions";

const props = defineProps<{ rootId: number }>();
const { t } = useI18n();
const togglePaid = usePaidToggle();

const chain = useQuery({
  queryKey: ["invoices", "chain", toRef(props, "rootId")],
  queryFn: () => loadChain(props.rootId),
});
const latest = computed(() => chain.data.value?.[0]);
const revisions = computed(() => buildRevisions(chain.data.value ?? []));

// the quote the invoice was converted from, named by its number
const quoteId = computed(() => latest.value?.QuoteId ?? null);
const quote = useQuoteQuery(quoteId);
const quoteNumber = computed(() => formatInvoiceNumber(quote.data.value?.QuoteNumber));

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
  latest.value ? { id: latest.value.Id, recipientId: latest.value.RecipientId } : null,
);

const editorOpen = ref(false);
</script>

<template>
  <PageHeader
    :title="latest ? number : t('invoiceDetailTitle')"
    :crumbs="[
      { label: t('menuInvoices'), to: listLocation('/invoices') },
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
  <Message v-else-if="chain.isError.value && !isNotFound(chain.error.value)" severity="error">
    {{ t("loadFailed") }}
  </Message>
  <Message v-else-if="!latest" severity="warn">{{ t("notFound") }}</Message>
  <div v-else class="flex flex-col gap-6">
    <Card>
      <template #content>
        <div class="text-lg font-semibold">{{ latest.RecipientName }}</div>
        <div class="whitespace-pre-line text-muted-color">{{ latest.RecipientAddress }}</div>
        <RouterLink
          v-if="quoteId != null && quoteNumber"
          :to="`/quotes/${quoteId}`"
          class="mt-3 inline-block text-primary hover:underline"
        >
          {{ t("invoiceFromQuote", [quoteNumber]) }}
        </RouterLink>
      </template>
    </Card>
    <RevisionList :revisions="revisions" kind="invoice" />
  </div>

  <InvoiceEditor v-model:visible="editorOpen" :target="editorTarget" />
</template>
