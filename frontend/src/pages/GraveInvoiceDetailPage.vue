<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import Tag from "primevue/tag";
import { computed, toRef } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink } from "vue-router";
import { fetchGraveDetails, fetchGraveInvoiceGroup } from "@/api/endpoints";
import { isNotFound } from "@/api/http";
import { loadChain } from "@/api/queries";
import GraveInvoiceEditor from "@/components/GraveInvoiceEditor.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import RevisionList from "@/components/RevisionList.vue";
import { listLocation } from "@/composables/useColumnFilters";
import { useGraveInvoiceEdit } from "@/composables/useGraveInvoiceEdit";
import { usePaidToggle } from "@/composables/usePaidToggle";
import { formatInvoiceNumber, formatShare } from "@/lib/format";
import { halfYearText } from "@/lib/halfYear";

const props = defineProps<{ graveId: number; rootId: number }>();
const { t } = useI18n();
const togglePaid = usePaidToggle();
const { visible: editorOpen, draft, open: openEditor } = useGraveInvoiceEdit();

const chain = useQuery({
  queryKey: ["invoices", "chain", toRef(props, "rootId")],
  queryFn: () => loadChain(props.rootId),
});
// only an invoice of the grave in the URL: another grave's, or an ordinary one,
// is "not found" rather than shown under the wrong grave's breadcrumbs
const latest = computed(() => {
  const first = chain.data.value?.[0];
  return first?.GraveId === props.graveId ? first : undefined;
});
const number = computed(() => formatInvoiceNumber(latest.value?.InvoiceNumber));

const latestId = computed(() => latest.value?.Id);
const group = useQuery({
  queryKey: ["graves", "invoice-group", latestId],
  queryFn: () => fetchGraveInvoiceGroup(latestId.value!),
  enabled: computed(() => latestId.value != null),
});
// a deleted grave's invoices can be read and downloaded, but neither paid nor revised
const grave = useQuery({
  queryKey: ["graves", "state", toRef(props, "graveId")],
  queryFn: () => fetchGraveDetails(props.graveId),
});
const deleted = computed(() => grave.data.value?.DeletedAt != null);
// the actions wait for the answer: until it is in, the grave may be a deleted one
const actionable = computed(() => grave.isSuccess.value && !deleted.value);
// the siblings of this half-year, minus the invoice shown
const others = computed(() =>
  (group.data.value?.Recipients ?? []).filter(
    (recipient) => recipient.InvoiceId !== latestId.value,
  ),
);
const halfYear = computed(() =>
  latest.value?.PeriodYear != null && latest.value.PeriodHalf != null
    ? halfYearText(latest.value.PeriodYear, latest.value.PeriodHalf)
    : "",
);
</script>

<template>
  <PageHeader
    :title="latest ? number : t('graveInvoiceDetailTitle')"
    :crumbs="[
      { label: t('menuGraves'), to: listLocation('/graves') },
      { label: group.data.value?.GraveNumber ?? t('graveDetailTitle'), to: `/graves/${graveId}` },
      { label: number || t('graveInvoiceDetailTitle') },
    ]"
  >
    <template v-if="latest" #actions>
      <PaidStatusTag :paid-on="latest.PaidOn" />
      <Tag v-if="deleted" :value="t('statusDeleted')" severity="warn" icon="pi pi-trash" />
      <Button
        v-if="actionable"
        :icon="latest.PaidOn ? 'pi pi-undo' : 'pi pi-wallet'"
        :label="latest.PaidOn ? t('actionMarkUnpaid') : t('actionMarkPaid')"
        severity="secondary"
        outlined
        @click="togglePaid(latest.Id, latest.PaidOn)"
      />
      <span v-if="actionable" v-tooltip.bottom="latest.PaidOn ? t('editBlockedPaid') : undefined">
        <Button
          :label="t('actionEdit')"
          icon="pi pi-pencil"
          :disabled="!!latest.PaidOn"
          @click="openEditor(latest.Id)"
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
        <div class="flex flex-wrap justify-between gap-6">
          <div>
            <div class="text-lg font-semibold">{{ latest.RecipientName }}</div>
            <div class="whitespace-pre-line text-muted-color">{{ latest.RecipientAddress }}</div>
          </div>
          <!-- three across from a tablet up, stacked on a phone: a long grave
               number has no space to break at, and fixed columns pushed the
               card past a phone's edge -->
          <dl class="m-0 grid min-w-0 grid-cols-1 gap-6 sm:grid-cols-3">
            <div class="min-w-0">
              <dt class="text-sm text-muted-color">{{ t("graveNumber") }}</dt>
              <dd class="m-0 [overflow-wrap:anywhere]">{{ group.data.value?.GraveNumber }}</dd>
            </div>
            <div>
              <dt class="text-sm text-muted-color">{{ t("halfYear") }}</dt>
              <dd class="m-0 whitespace-nowrap">{{ halfYear }}</dd>
            </div>
            <div>
              <dt class="text-sm text-muted-color">{{ t("sharePercent") }}</dt>
              <dd class="m-0 tabular-nums">{{ formatShare(latest.SharePercent) }}</dd>
            </div>
          </dl>
        </div>
      </template>
    </Card>

    <Card v-if="others.length">
      <template #title>{{ t("otherRecipients") }}</template>
      <template #content>
        <ul class="m-0 flex list-none flex-col gap-1 p-0">
          <li v-for="recipient in others" :key="recipient.InvoiceId">
            <RouterLink
              :to="`/graves/${graveId}/invoices/${recipient.RootId}`"
              class="flex items-center justify-between rounded-lg px-3 py-2 text-color no-underline hover:bg-emphasis"
            >
              <span>{{ recipient.Name }}</span>
              <i class="pi pi-angle-right" aria-hidden="true" />
            </RouterLink>
          </li>
        </ul>
      </template>
    </Card>

    <RevisionList :chain="chain.data.value ?? []" />
  </div>

  <GraveInvoiceEditor v-model:visible="editorOpen" :draft="draft" />
</template>
