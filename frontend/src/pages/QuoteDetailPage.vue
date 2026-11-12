<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import { computed, ref, toRef } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink, useRouter } from "vue-router";
import { QuoteState } from "@/api/generated";
import { isNotFound } from "@/api/http";
import { loadQuoteChain } from "@/api/queries";
import InvoiceEditor from "@/components/InvoiceEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import QuoteStateTag from "@/components/QuoteStateTag.vue";
import RevisionList from "@/components/RevisionList.vue";
import { listLocation } from "@/composables/useColumnFilters";
import { useQuoteActions } from "@/composables/useQuoteActions";
import { formatInvoiceNumber } from "@/lib/format";
import { buildQuoteRevisions } from "@/lib/revisions";

const props = defineProps<{ rootId: number }>();
const { t } = useI18n();
const router = useRouter();
const actions = useQuoteActions();

const chain = useQuery({
  queryKey: ["quotes", "chain", toRef(props, "rootId")],
  queryFn: () => loadQuoteChain(props.rootId),
});
const latest = computed(() => chain.data.value?.[0]);
const revisions = computed(() => buildQuoteRevisions(chain.data.value ?? []));
const number = computed(() => formatInvoiceNumber(latest.value?.QuoteNumber));
const isOpen = computed(() => latest.value?.State === QuoteState.Open);
const isDeclined = computed(() => latest.value?.State === QuoteState.Declined);

const editorOpen = ref(false);
const editorTarget = computed(() =>
  latest.value ? { id: latest.value.Id, recipientId: latest.value.RecipientId } : null,
);

// the actions address the latest version, which stands for the whole quote
async function remove(id: number): Promise<void> {
  if (await actions.remove(id, number.value)) {
    await router.push(listLocation("/quotes"));
  }
}
</script>

<template>
  <PageHeader
    :title="latest ? number : t('quoteDetailTitle')"
    :crumbs="[
      { label: t('menuQuotes'), to: listLocation('/quotes') },
      { label: number || t('quoteDetailTitle') },
    ]"
  >
    <template v-if="latest" #actions>
      <QuoteStateTag :state="latest.State" />
      <template v-if="isOpen">
        <Button
          :label="t('actionDeclineQuote')"
          icon="pi pi-times-circle"
          severity="secondary"
          outlined
          @click="actions.decline(latest.Id)"
        />
        <Button :label="t('actionEdit')" icon="pi pi-pencil" outlined @click="editorOpen = true" />
        <Button
          :label="t('actionConvertQuote')"
          icon="pi pi-file-export"
          @click="actions.convert(latest.Id, number)"
        />
      </template>
      <Button
        v-if="isDeclined"
        :label="t('actionReopenQuote')"
        icon="pi pi-undo"
        severity="secondary"
        outlined
        @click="actions.reopen(latest.Id)"
      />
      <!-- a converted quote is closed: it only links to its invoice -->
      <Button
        v-if="latest.State !== QuoteState.Converted"
        :label="t('actionDeleteQuote')"
        icon="pi pi-trash"
        severity="danger"
        outlined
        @click="remove(latest.Id)"
      />
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
        <!-- the invoice the quote became, under its first number -->
        <RouterLink
          v-if="latest.InvoiceId != null && latest.InvoiceNumber"
          :to="`/invoices/${latest.InvoiceId}`"
          class="mt-3 inline-block text-primary hover:underline"
        >
          {{ t("quoteConvertedInto", [formatInvoiceNumber(latest.InvoiceNumber)]) }}
        </RouterLink>
      </template>
    </Card>
    <RevisionList :revisions="revisions" kind="quote" />
  </div>

  <InvoiceEditor v-model:visible="editorOpen" :target="editorTarget" kind="quote" />
</template>
