<script setup lang="ts">
import Button from "primevue/button";
import Checkbox from "primevue/checkbox";
import InputNumber from "primevue/inputnumber";
import Textarea from "primevue/textarea";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import type { InvoiceItem } from "@/api/generated";
import { useItemsQuery } from "@/api/queries";
import { formatAmount, formatVatRate } from "@/lib/format";
import { vDecimalPoint } from "@/directives/decimalPoint";
import { type DraftLine, newLine } from "@/lib/lines";
import { lineNet, roundCents } from "@/lib/money";
import { quarterLabelKey } from "@/lib/quarter";
import ItemPicker from "./ItemPicker.vue";

const lines = defineModel<DraftLine[]>({ required: true });
/** Ordinary invoices and quotes print a note under a line; a grave does not. */
const props = defineProps<{ withComments?: boolean }>();

const { t } = useI18n();
const items = useItemsQuery(ref(""));
const byId = computed(() => new Map((items.data.value ?? []).map((item) => [item.Id, item])));

function item(line: DraftLine): InvoiceItem | undefined {
  return line.itemId == null ? undefined : byId.value.get(line.itemId);
}

/** The item's description, rate and quarter: what decides where and at which
 *  rate the line is billed, without two more columns the drawer has no room
 *  for on a tablet. */
function caption(line: DraftLine): string {
  const picked = item(line);
  if (!picked) {
    return "";
  }
  return [
    picked.Description,
    `${formatVatRate(picked.VatRate)} %`,
    t(quarterLabelKey(picked.Quarter)),
  ].join(" · ");
}

function defaultPriceText(line: DraftLine): string {
  const price = item(line)?.DefaultPrice;
  return price == null ? "" : formatAmount(price);
}

/** A picked item fills in its default price, or clears the field when it has
 *  none. */
function onItemPicked(line: DraftLine): void {
  line.unitPrice = item(line)?.DefaultPrice ?? null;
}

function addLine(): void {
  lines.value = [...lines.value, newLine()];
}

function removeLine(key: number): void {
  lines.value = lines.value.filter((line) => line.key !== key);
}

/** An estimate while typing; the invoice's own totals come from the backend.
 *  Rounded line by line as an invoice is -- a VAT-included price back to its
 *  net per unit, each line's net and VAT to the cent -- so it matches the
 *  saved invoice in the usual case rather than drifting by a cent or two. */
const totals = computed(() => {
  let net = 0;
  let vat = 0;
  for (const line of lines.value) {
    const rate = item(line)?.VatRate;
    const price = line.unitPrice ?? item(line)?.DefaultPrice ?? null;
    if (rate == null || price == null || line.count == null) {
      continue;
    }
    const unitNet = line.priceIncludesVat
      ? roundCents(roundCents(price) / (1 + rate / 100))
      : price;
    const lineNetValue = lineNet(line.count, unitNet);
    net += lineNetValue;
    vat += roundCents((lineNetValue * rate) / 100);
  }
  return { net: roundCents(net), gross: roundCents(net + vat) };
});
</script>

<template>
  <div class="flex flex-col gap-3">
    <div
      class="hidden gap-3 px-1 text-sm font-medium text-muted-color md:grid md:grid-cols-[minmax(14rem,1fr)_6rem_10rem_8rem_2.5rem]"
      aria-hidden="true"
    >
      <span>{{ t("itemNumber") }}</span>
      <span>{{ t("itemCount") }}</span>
      <span>{{ t("itemPrice") }}</span>
      <span>{{ t("priceIncludesVatShort") }}</span>
      <span />
    </div>

    <div
      v-for="(line, index) in lines"
      :key="line.key"
      class="grid grid-cols-2 gap-3 rounded-lg border border-surface p-3 md:grid-cols-[minmax(14rem,1fr)_6rem_10rem_8rem_2.5rem] md:items-start md:border-0 md:p-1"
    >
      <div class="col-span-2 flex min-w-0 flex-col gap-1 md:col-span-1">
        <ItemPicker
          v-model="line.itemId"
          :aria-label="`${t('itemNumber')} ${index + 1}`"
          @change="onItemPicked(line)"
        />
        <small v-if="item(line)" class="px-1 text-muted-color">{{ caption(line) }}</small>
      </div>
      <!-- The column headers are hidden on a phone, so each field carries its own
           visible caption there; the accessible name stays the numbered label. -->
      <div class="flex min-w-0 flex-col gap-1">
        <span class="text-xs text-muted-color md:hidden" aria-hidden="true">
          {{ t("itemCount") }}
        </span>
        <!-- A count is whole, but "1,5" or "-3" is kept as typed so the save can
             say so; a field that dropped the comma or the sign turned them into
             15 and 3 without a word. -->
        <InputNumber
          v-model="line.count"
          v-decimal-point
          locale="de-DE"
          :max-fraction-digits="2"
          :use-grouping="false"
          :aria-label="`${t('itemCount')} ${index + 1}`"
          fluid
        />
      </div>
      <div class="flex min-w-0 flex-col gap-1">
        <span class="text-xs text-muted-color md:hidden" aria-hidden="true">
          {{ t("itemPrice") }}
        </span>
        <!-- Up to four decimals: a grave invoice reopens net prices kept at three
             on purpose, and rounding them here would change an untouched line. -->
        <InputNumber
          v-model="line.unitPrice"
          v-decimal-point
          locale="de-DE"
          :min-fraction-digits="2"
          :max-fraction-digits="4"
          :placeholder="defaultPriceText(line)"
          :aria-label="`${t('itemPrice')} ${index + 1}`"
          fluid
        />
      </div>
      <label class="flex items-center gap-2 self-end pb-2 md:self-auto md:pt-2 md:pb-0">
        <Checkbox v-model="line.priceIncludesVat" binary />
        <span class="text-sm md:sr-only">{{ t("priceIncludesVatShort") }}</span>
      </label>
      <Button
        v-tooltip.left="t('deleteLine')"
        icon="pi pi-trash"
        severity="danger"
        text
        rounded
        class="justify-self-end"
        :aria-label="t('deleteLine')"
        @click="removeLine(line.key)"
      />
      <!-- under the item, as the document prints it under the description -->
      <Textarea
        v-if="props.withComments"
        v-model="line.comment"
        rows="1"
        auto-resize
        class="col-span-2 md:col-span-4"
        :placeholder="t('lineCommentPlaceholder')"
        :aria-label="`${t('lineComment')} ${index + 1}`"
      />
    </div>

    <div class="flex flex-wrap items-center justify-between gap-3">
      <Button :label="t('addLine')" icon="pi pi-plus" outlined size="small" @click="addLine" />
      <span class="text-sm text-muted-color tabular-nums">
        {{ t("linesTotal", [formatAmount(totals.net), formatAmount(totals.gross)]) }}
      </span>
    </div>
  </div>
</template>
