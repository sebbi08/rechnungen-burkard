<script setup lang="ts">
import { useQuery, useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import InputNumber from "primevue/inputnumber";
import InputText from "primevue/inputtext";
import Message from "primevue/message";
import Select from "primevue/select";
import Tag from "primevue/tag";
import { useToast } from "primevue/usetoast";
import { computed, reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink } from "vue-router";
import { getItemGraves, type ItemGraveGroup } from "@/api/endpoints";
import type { InvoiceItem } from "@/api/generated";
import { ApiError } from "@/api/http";
import { odata } from "@/api/odata";
import { useVatRatesQuery } from "@/api/queries";
import { useErrorToast } from "@/composables/useErrorToast";
import { vDecimalPoint } from "@/directives/decimalPoint";
import { formatAmount, formatVatRate } from "@/lib/format";
import {
  QUARTERS,
  type QuarterName,
  quarterLabelKey,
  toBillingQuarter,
  toQuarterName,
} from "@/lib/quarter";

const props = defineProps<{ item: InvoiceItem | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const { showError } = useErrorToast();
const toast = useToast();
const queryClient = useQueryClient();
const vatRates = useVatRatesQuery();

const rateOptions = computed(() =>
  (vatRates.data.value ?? []).map((rate) => ({
    value: rate.Rate,
    label: `${formatVatRate(rate.Rate)} %`,
  })),
);

const quarterOptions = computed(() => [
  { value: null, label: t("quarterNone") },
  ...QUARTERS.map((value) => ({ value, label: t(quarterLabelKey(value)) })),
]);

const form = reactive({
  Number: "",
  Description: "",
  VatRate: null as number | null,
  Quarter: null as QuarterName | null,
  DefaultPrice: null as number | null,
});

watch(visible, (open) => {
  if (open) {
    Object.assign(form, {
      Number: props.item?.Number ?? "",
      Description: props.item?.Description ?? "",
      VatRate: props.item?.VatRate ?? null,
      Quarter: toQuarterName(props.item?.Quarter),
      // empty means "no default price"; 0 is a real price
      DefaultPrice: props.item?.DefaultPrice ?? null,
    });
    void vatRates.refetch();
  }
});

const itemId = computed(() => props.item?.Id ?? null);
const graves = useQuery({
  queryKey: ["items", "graves", itemId],
  queryFn: () => getItemGraves(itemId.value!),
  enabled: computed(() => visible.value && itemId.value != null),
});
const linked = computed(() => graves.data.value?.Linked ?? false);
// Until the answer is in, a rate picked here could be one the graves forbid:
// the select would lock on it once they load, and the save would be refused.
const rateLocked = computed(() => linked.value || (itemId.value != null && graves.isPending.value));

function groupLabel(group: ItemGraveGroup): string {
  const price =
    group.UnitPrice == null
      ? t("itemPriceNone")
      : t(group.IsDefault ? "itemPriceDefault" : "itemPriceOwn", [formatAmount(group.UnitPrice)]);
  return group.PriceIncludesVat ? `${price} (${t("priceIncludesVatShort")})` : price;
}

const saving = ref(false);
async function save(): Promise<void> {
  if (form.VatRate == null) {
    // sent as 0 it came back as "rate not configured", which reads as a fault
    showError(new ApiError("VatRateRequired"));
    return;
  }
  saving.value = true;
  // trimmed, as the recipient number is: "X" and "X " were two items
  const payload = {
    Number: form.Number.trim(),
    Description: form.Description.trim(),
    VatRate: form.VatRate,
    Quarter: toBillingQuarter(form.Quarter),
    DefaultPrice: form.DefaultPrice,
  };
  try {
    if (props.item) {
      await odata.InvoiceItems(props.item.Id).patch(payload).execute();
    } else {
      await odata.InvoiceItems().create(payload).execute();
    }
    await queryClient.invalidateQueries();
    visible.value = false;
    toast.add({ severity: "success", summary: t("saved"), life: 3000 });
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="item ? t('editItem') : t('createItem')"
    :style="{ width: '30rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <form id="itemForm" class="flex flex-col gap-4" @submit.prevent="save">
      <div class="flex flex-col gap-2">
        <label for="itemNumber" class="font-medium">{{ t("itemNumber") }}</label>
        <InputText id="itemNumber" v-model="form.Number" autofocus />
      </div>
      <div class="flex flex-col gap-2">
        <label for="itemDescription" class="font-medium">{{ t("itemDescription") }}</label>
        <InputText id="itemDescription" v-model="form.Description" />
      </div>
      <div class="flex flex-col gap-2">
        <label for="itemVatRate" class="font-medium">{{ t("vatRate") }}</label>
        <Select
          v-model="form.VatRate"
          input-id="itemVatRate"
          :options="rateOptions"
          option-value="value"
          option-label="label"
          :disabled="rateLocked"
        />
      </div>
      <div class="flex flex-col gap-2">
        <label for="itemQuarter" class="font-medium">{{ t("itemQuarter") }}</label>
        <Select
          v-model="form.Quarter"
          input-id="itemQuarter"
          :options="quarterOptions"
          option-value="value"
          option-label="label"
          :disabled="rateLocked"
        />
        <small v-if="linked" class="text-muted-color">{{ t("itemVatRateFixed") }}</small>
      </div>
      <div class="flex flex-col gap-2">
        <label for="itemDefaultPrice" class="font-medium">{{ t("itemDefaultPrice") }}</label>
        <InputNumber
          v-model="form.DefaultPrice"
          v-decimal-point
          input-id="itemDefaultPrice"
          locale="de-DE"
          :min-fraction-digits="2"
          :max-fraction-digits="2"
          suffix=" €"
        />
      </div>

      <section v-if="item" class="flex flex-col gap-2">
        <h3 class="m-0 text-base font-semibold">{{ t("itemGravesTitle") }}</h3>
        <!-- linked, yet nothing listed: only deleted graves still link the item -->
        <Message
          v-if="graves.data.value?.Linked && graves.data.value.Groups.length === 0"
          severity="secondary"
          size="small"
        >
          {{ t("itemGravesOnlyDeleted") }}
        </Message>
        <Message
          v-if="graves.data.value && !graves.data.value.Linked"
          severity="secondary"
          size="small"
        >
          {{ t("itemGravesNone") }}
        </Message>
        <div
          v-for="(group, index) in graves.data.value?.Groups ?? []"
          :key="index"
          class="flex flex-col gap-1"
        >
          <span class="text-sm font-medium">{{ groupLabel(group) }}</span>
          <div class="flex flex-wrap gap-x-3 gap-y-1">
            <span
              v-for="grave in group.Graves"
              :key="grave.Id"
              class="inline-flex items-center gap-1"
            >
              <RouterLink
                :to="`/graves/${grave.Id}`"
                class="text-primary hover:underline"
                @click="visible = false"
              >
                {{ grave.GraveNumber }}
              </RouterLink>
              <Tag
                v-if="grave.IsCooperative"
                :value="t('statusCooperative')"
                severity="secondary"
              />
            </span>
          </div>
        </div>
      </section>
    </form>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        type="submit"
        form="itemForm"
        :label="t('save')"
        icon="pi pi-check"
        :loading="saving"
      />
    </template>
  </Dialog>
</template>
