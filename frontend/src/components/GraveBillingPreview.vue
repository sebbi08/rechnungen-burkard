<script setup lang="ts">
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import Tag from "primevue/tag";
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import type { BillingPreview, BillingPreviewInvoice, GraveBillingProblem } from "@/api/endpoints";
import { formatAmount, formatVatRate } from "@/lib/format";
import { halfYearText } from "@/lib/halfYear";

const props = defineProps<{
  preview: BillingPreview | null;
  pending: boolean;
  failed: boolean;
  assumesOneRecipient?: boolean;
}>();

const { t } = useI18n();

const suggested = computed(
  () => new Set((props.preview?.LineSuggestions ?? []).map((entry) => entry.LineIndex)),
);

function title(invoice: BillingPreviewInvoice): string {
  const key = `billingKind${invoice.Kind}`;
  return invoice.Year == null ? t(key) : t(key, [halfYearText(invoice.Year, invoice.Half)]);
}

function problemText(problem: GraveBillingProblem): string {
  const recipients = props.preview?.Recipients ?? 1;
  return t(recipients === 1 ? "pricesDoNotDivideLineOne" : "pricesDoNotDivideLine", [
    formatVatRate(problem.VatRate),
    formatAmount(problem.Amount),
    recipients,
  ]);
}

/** Without a suggestion on any of its lines, the card says what to do itself. */
function needsAdvice(invoice: BillingPreviewInvoice): boolean {
  return !invoice.Ok && !invoice.Lines.some((line) => suggested.value.has(line.LineIndex));
}
</script>

<template>
  <section class="flex flex-col gap-3" :aria-busy="pending">
    <div class="flex items-center gap-2">
      <h3 class="m-0 text-base font-semibold">{{ t("billingPreviewTitle") }}</h3>
      <ProgressSpinner
        v-if="pending"
        style="width: 1.25rem; height: 1.25rem"
        stroke-width="6"
        :aria-label="t('billingPreviewChecking')"
      />
    </div>
    <Message v-if="failed" severity="warn" size="small">
      {{ t("billingPreviewUnavailable") }}
    </Message>
    <Message v-if="preview && assumesOneRecipient" severity="info" size="small">
      {{ t("billingPreviewAssumesOne") }}
    </Message>
    <p v-if="!preview && !failed" class="m-0 text-sm text-muted-color">
      {{ t("billingPreviewHint") }}
    </p>
    <div v-if="preview" class="grid gap-3 sm:grid-cols-2">
      <article
        v-for="(invoice, index) in preview.Invoices"
        :key="`${invoice.Kind}-${index}`"
        role="group"
        :aria-label="title(invoice)"
        class="flex min-w-0 flex-col gap-2 rounded-lg border p-3"
        :class="invoice.Ok ? 'border-surface' : 'border-red-500'"
        :data-billing-invalid="invoice.Ok ? undefined : ''"
        :tabindex="invoice.Ok ? undefined : -1"
      >
        <header class="flex flex-wrap items-start justify-between gap-2">
          <div class="flex min-w-0 flex-col">
            <span class="font-medium">{{ title(invoice) }}</span>
            <small v-if="invoice.Months < 6" class="text-muted-color">
              {{ t("billingMonths", [invoice.Months]) }}
            </small>
          </div>
          <Tag
            :value="invoice.Ok ? t('billingOk') : t('billingNotOk')"
            :severity="invoice.Ok ? 'success' : 'danger'"
            :icon="invoice.Ok ? 'pi pi-check' : 'pi pi-times'"
          />
        </header>
        <p v-if="invoice.Lines.length === 0" class="m-0 text-sm text-muted-color">
          {{ t("billingNothing") }}
        </p>
        <table v-else class="w-full text-sm">
          <tbody>
            <tr v-for="line in invoice.Lines" :key="line.LineIndex">
              <td class="py-0.5 pr-2 align-top whitespace-nowrap">{{ line.ItemNumber }}</td>
              <td class="py-0.5 pr-2 align-top break-words">
                {{ line.Description }}
                <span class="text-muted-color">· {{ formatVatRate(line.VatRate) }} %</span>
              </td>
              <td class="py-0.5 pr-2 text-right align-top tabular-nums whitespace-nowrap">
                {{ line.Count }} ×
              </td>
              <td class="py-0.5 text-right align-top tabular-nums whitespace-nowrap">
                {{ formatAmount(line.Price) }}
              </td>
            </tr>
          </tbody>
        </table>
        <dl
          v-if="invoice.Lines.length > 0"
          class="m-0 grid grid-cols-[1fr_auto] gap-x-3 text-sm tabular-nums"
        >
          <dt class="text-muted-color">{{ t("billingNetLabel") }}</dt>
          <dd class="m-0 text-right">{{ formatAmount(invoice.NetTotal) }}</dd>
          <template v-if="invoice.GrossTotal != null">
            <dt class="text-muted-color">{{ t("billingGrossLabel") }}</dt>
            <dd class="m-0 text-right">{{ formatAmount(invoice.GrossTotal) }}</dd>
            <dt class="text-muted-color">
              {{ t("billingPerRecipientLabel", [`1/${preview.Recipients}`]) }}
            </dt>
            <dd class="m-0 text-right">{{ formatAmount(invoice.PerRecipient) }}</dd>
          </template>
        </dl>
        <ul
          v-if="!invoice.Ok"
          class="m-0 flex list-none flex-col gap-1 p-0 text-sm text-red-700 dark:text-red-400"
        >
          <li v-for="problem in invoice.Problems" :key="problem.VatRate">
            {{ problemText(problem) }}
          </li>
          <li v-if="needsAdvice(invoice)">{{ t("pricesAdjustAPrice") }}</li>
        </ul>
      </article>
    </div>
  </section>
</template>
