<script setup lang="ts">
import { useQuery } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import Tag from "primevue/tag";
import { computed, ref, toRef } from "vue";
import { useI18n } from "vue-i18n";
import { isNotFound } from "@/api/http";
import { loadGraveDetail } from "@/api/queries";
import GraveBillingPreview from "@/components/GraveBillingPreview.vue";
import GraveEditor from "@/components/GraveEditor.vue";
import GraveInvoiceEditor from "@/components/GraveInvoiceEditor.vue";
import InvoiceRowActions from "@/components/InvoiceRowActions.vue";
import PageHeader from "@/components/PageHeader.vue";
import PaidStatusTag from "@/components/PaidStatusTag.vue";
import { type PreviewInput, useGraveBillingPreview } from "@/composables/useGraveBillingPreview";
import { useGraveInvoiceEdit } from "@/composables/useGraveInvoiceEdit";
import {
  formatAmount,
  formatDate,
  formatInvoiceNumber,
  formatMonth,
  formatRevisionBadge,
  formatVatRate,
} from "@/lib/format";
import { currentHalfYear, halfYearHasBilling, halfYearText } from "@/lib/halfYear";

const props = defineProps<{ graveId: number }>();
const { t } = useI18n();

const detail = useQuery({
  queryKey: ["graves", "detail", toRef(props, "graveId")],
  queryFn: () => loadGraveDetail(props.graveId),
});
const grave = computed(() => detail.data.value?.grave);
const recipients = computed(() => grave.value?.Recipients.map((link) => link.Recipient) ?? []);
const [currentYear, currentHalf] = currentHalfYear();
const currentLabel = computed(() =>
  t("currentHalfYearStatus", [halfYearText(currentYear, currentHalf)]),
);

const inTerm = computed(() =>
  grave.value
    ? halfYearHasBilling(
        grave.value.StartDate,
        grave.value.EndDate,
        currentYear,
        currentHalf,
        grave.value.Items.map((link) => link.Item.Quarter),
      )
    : true,
);
const previewInput = computed<PreviewInput | null>(() => {
  const current = grave.value;
  if (!current || current.Items.length === 0) {
    return null;
  }
  return {
    recipients: Math.max(1, current.Recipients.length),
    startMonth: current.StartDate.slice(0, 7),
    endMonth: current.EndDate?.slice(0, 7) ?? null,
    lines: current.Items.map((link) => ({
      itemId: link.Item.Id,
      count: link.Count,
      unitPrice: link.UnitPrice,
      priceIncludesVat: link.PriceIncludesVat,
    })),
    keys: current.Items.map((_, index) => index),
  };
});
const billing = useGraveBillingPreview(previewInput);

const editorOpen = ref(false);
const {
  visible: graveInvoiceOpen,
  draft: graveInvoiceDraft,
  open: openGraveInvoice,
} = useGraveInvoiceEdit();
</script>

<template>
  <PageHeader
    :title="grave?.GraveNumber ?? t('graveDetailTitle')"
    :crumbs="[
      { label: t('menuGraves'), to: '/graves' },
      { label: grave?.GraveNumber ?? t('graveDetailTitle') },
    ]"
  >
    <template v-if="grave" #actions>
      <Button :label="t('editGrave')" icon="pi pi-pencil" @click="editorOpen = true" />
    </template>
  </PageHeader>

  <div v-if="detail.isPending.value" class="flex justify-center p-10"><ProgressSpinner /></div>
  <Message v-else-if="detail.isError.value && !isNotFound(detail.error.value)" severity="error">
    {{ t("loadFailed") }}
  </Message>
  <Message v-else-if="detail.isError.value || !grave || !detail.data.value" severity="warn">
    {{ t("notFound") }}
  </Message>
  <div v-else class="flex flex-col gap-6">
    <Card>
      <template #content>
        <dl class="m-0 grid grid-cols-2 gap-4 md:grid-cols-4">
          <div>
            <dt class="text-sm text-muted-color">{{ t("graveFieldNumber") }}</dt>
            <dd class="m-0">{{ grave.FieldNumber || "–" }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted-color">{{ t("graveSiteName") }}</dt>
            <dd class="m-0">{{ grave.SiteName || "–" }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted-color">{{ t("gravePosition") }}</dt>
            <dd class="m-0">{{ grave.Position ? t(`gravePosition${grave.Position}`) : "–" }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted-color">{{ t("startDate") }}</dt>
            <dd class="m-0 tabular-nums">{{ formatMonth(grave.StartDate) }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted-color">{{ t("endDate") }}</dt>
            <dd class="m-0 tabular-nums">{{ formatMonth(grave.EndDate) || "–" }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted-color">{{ t("graveCreatedAt") }}</dt>
            <dd class="m-0 tabular-nums">{{ formatDate(grave.CreatedAt) }}</dd>
          </div>
          <div>
            <dt class="text-sm text-muted-color">{{ currentLabel }}</dt>
            <dd class="m-0">
              <Tag
                :value="
                  detail.data.value.currentInvoiced
                    ? t('statusInvoiced')
                    : inTerm
                      ? t('statusOpen')
                      : t('statusOutsideTerm')
                "
                :severity="
                  detail.data.value.currentInvoiced ? 'success' : inTerm ? 'danger' : 'secondary'
                "
                role="img"
                :aria-label="
                  detail.data.value.currentInvoiced
                    ? t('statusInvoicedAnnouncement')
                    : inTerm
                      ? t('statusOpenAnnouncement')
                      : t('statusOutsideTermAnnouncement')
                "
              />
            </dd>
          </div>
        </dl>
      </template>
    </Card>

    <Card>
      <template #title>{{ t("graveItems") }}</template>
      <template #content>
        <DataTable :value="detail.data.value.lines" size="small">
          <template #empty>{{ t("noData") }}</template>
          <Column field="Number" :header="t('itemNumber')" />
          <Column
            field="Description"
            :header="t('itemDescription')"
            header-class="hidden sm:table-cell"
            body-class="hidden sm:table-cell"
          />
          <Column
            field="Count"
            :header="t('itemCount')"
            header-class="col-num"
            body-class="text-right tabular-nums"
          />
          <Column
            :header="t('itemPrice')"
            header-class="col-num"
            body-class="text-right tabular-nums whitespace-nowrap"
          >
            <template #body="{ data }">{{ formatAmount(data.Price) }}</template>
          </Column>
          <Column
            :header="t('vatRate')"
            header-class="hidden md:table-cell col-num"
            body-class="hidden md:table-cell text-right"
          >
            <template #body="{ data }">{{ formatVatRate(data.VatRate) }} %</template>
          </Column>
          <Column
            :header="t('priceIncludesVatColumn')"
            header-class="hidden md:table-cell"
            body-class="hidden md:table-cell"
          >
            <template #body="{ data }">{{ data.PriceIncludesVat ? t("yes") : t("no") }}</template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <Card v-if="(grave?.Items.length ?? 0) > 0">
      <template #content>
        <GraveBillingPreview
          :assumes-one-recipient="(grave?.Recipients.length ?? 0) === 0"
          :preview="billing.preview.value"
          :pending="billing.pending.value"
          :failed="billing.failed.value"
        />
      </template>
    </Card>

    <Card>
      <template #title>{{ t("graveRecipients") }}</template>
      <template #content>
        <DataTable :value="recipients" size="small">
          <Column field="Name" :header="t('recipientName')" />
          <Column :header="t('recipientAddress')" body-class="whitespace-pre-line">
            <template #body="{ data }">{{ data.Address }}</template>
          </Column>
          <Column
            :header="t('recipientEmail')"
            header-class="hidden md:table-cell"
            body-class="hidden md:table-cell [overflow-wrap:anywhere]"
          >
            <template #body="{ data }">{{ data.Email ?? "" }}</template>
          </Column>
        </DataTable>
      </template>
    </Card>

    <Card>
      <template #title>{{ t("graveInvoices") }}</template>
      <template #content>
        <DataTable :value="detail.data.value.invoices" data-key="Id" size="small">
          <template #empty>{{ t("noGraveInvoices") }}</template>
          <Column :header="t('halfYear')" body-class="whitespace-nowrap">
            <template #body="{ data }">{{
              halfYearText(data.PeriodYear, data.PeriodHalf)
            }}</template>
          </Column>
          <Column :header="t('recipientName')">
            <template #body="{ data }">
              {{ data.RecipientName }}
              <!-- on a phone the payment state sits under the name instead of in its own column -->
              <div class="mt-1 sm:hidden"><PaidStatusTag :paid-on="data.PaidOn" /></div>
            </template>
          </Column>
          <Column
            :header="t('invoiceNumber')"
            header-class="hidden md:table-cell"
            body-class="hidden md:table-cell tabular-nums whitespace-nowrap"
          >
            <template #body="{ data }">
              {{ formatInvoiceNumber(data.InvoiceNumber) }}
              <Tag
                v-if="data.RevisionNumber > 0"
                :value="formatRevisionBadge(data.RevisionNumber)"
                severity="secondary"
                class="ml-1"
              />
            </template>
          </Column>
          <Column
            :header="t('invoiceDate')"
            header-class="hidden xl:table-cell"
            body-class="hidden xl:table-cell tabular-nums"
          >
            <template #body="{ data }">{{ formatDate(data.GeneratedAt) }}</template>
          </Column>
          <Column
            :header="t('grossTotal')"
            header-class="hidden sm:table-cell col-num"
            body-class="hidden sm:table-cell text-right tabular-nums whitespace-nowrap"
          >
            <template #body="{ data }">{{ formatAmount(data.GrossTotal) }}</template>
          </Column>
          <Column
            :header="t('paidStatus')"
            header-class="hidden sm:table-cell"
            body-class="hidden sm:table-cell"
          >
            <template #body="{ data }"><PaidStatusTag :paid-on="data.PaidOn" /></template>
          </Column>
          <Column class="w-0 whitespace-nowrap">
            <template #body="{ data }">
              <InvoiceRowActions
                :invoice-id="data.Id"
                :paid-on="data.PaidOn"
                :details-to="`/graves/${graveId}/invoices/${data.RootId}`"
                :row-label="`${halfYearText(data.PeriodYear, data.PeriodHalf)} ${data.RecipientName}`"
                @edit="openGraveInvoice(data.Id)"
              />
            </template>
          </Column>
        </DataTable>
      </template>
    </Card>
  </div>

  <GraveEditor v-model:visible="editorOpen" :grave-id="graveId" />
  <GraveInvoiceEditor v-model:visible="graveInvoiceOpen" :draft="graveInvoiceDraft" />
</template>
