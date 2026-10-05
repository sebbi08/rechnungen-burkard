<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import DatePicker from "primevue/datepicker";
import Drawer from "primevue/drawer";
import InputText from "primevue/inputtext";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import Select from "primevue/select";
import { computed, nextTick, reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { fetchGraveDetails, type InvoiceLineInput, saveGrave } from "@/api/endpoints";
import { ApiError } from "@/api/http";
import { useItemsQuery, useRecipientsQuery } from "@/api/queries";
import { useDrawerFocus } from "@/composables/useDrawerFocus";
import { useErrorToast } from "@/composables/useErrorToast";
import { type PreviewInput, useGraveBillingPreview } from "@/composables/useGraveBillingPreview";
import { MAX_RECIPIENTS, previewLines } from "@/lib/billing";
import { parseIsoDate, toIsoMonth } from "@/lib/format";
import { GRAVE_POSITIONS, type GravePositionName } from "@/lib/gravePosition";
import { type DraftLine, newLine, toLineInputs } from "@/lib/lines";
import GraveBillingPreview from "./GraveBillingPreview.vue";
import LineEditor from "./LineEditor.vue";
import RecipientPicker from "./RecipientPicker.vue";

const props = defineProps<{ graveId: number | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const { showError } = useErrorToast();
const drawerFocus = useDrawerFocus();
const formEl = ref<HTMLFormElement | null>(null);
const queryClient = useQueryClient();
const allRecipients = useRecipientsQuery(ref(""));

const form = reactive({
  GraveNumber: "",
  FieldNumber: "",
  SiteName: "",
  Position: null as GravePositionName | null,
  StartDate: null as Date | null,
  EndDate: null as Date | null,
});
const positionOptions = computed(() =>
  GRAVE_POSITIONS.map((value) => ({ value, label: t(`gravePosition${value}`) })),
);
const lines = ref<DraftLine[]>([]);
const recipients = ref<{ id: number; name: string }[]>([]);
const linkRecipientId = ref<number | null>(null);
const loading = ref(false);
const saving = ref(false);
// Which opening a load belongs to. An answer that arrives after the drawer was
// closed -- or reopened for another grave, or for a new one -- is dropped:
// filled in, it turned a new grave into a copy of the old one.
let opening = 0;

watch(visible, async (open) => {
  const current = ++opening;
  if (open) {
    drawerFocus.remember();
  }
  if (!open) {
    loading.value = false;
    return;
  }
  Object.assign(form, {
    GraveNumber: "",
    FieldNumber: "",
    SiteName: "",
    Position: null,
    StartDate: null,
    EndDate: null,
  });
  lines.value = [];
  recipients.value = [];
  linkRecipientId.value = null;
  if (props.graveId == null) {
    return;
  }
  loading.value = true;
  try {
    const grave = await fetchGraveDetails(props.graveId);
    if (current !== opening) {
      return;
    }
    Object.assign(form, {
      GraveNumber: grave.GraveNumber,
      FieldNumber: grave.FieldNumber,
      SiteName: grave.SiteName,
      Position: grave.Position,
      StartDate: parseIsoDate(grave.StartDate),
      EndDate: parseIsoDate(grave.EndDate),
    });
    // a link price is shown as stored -- already the gross on a VAT-included line
    lines.value = grave.Items.map((link) =>
      newLine({
        itemId: link.Item.Id,
        count: link.Count,
        unitPrice: link.UnitPrice,
        priceIncludesVat: link.PriceIncludesVat,
      }),
    );
    recipients.value = grave.Recipients.map((link) => ({
      id: link.Recipient.Id,
      name: link.Recipient.Name,
    }));
  } catch (error) {
    if (current === opening) {
      showError(error);
      visible.value = false;
    }
  } finally {
    if (current === opening) {
      loading.value = false;
      void drawerFocus.focusFirst(formEl.value);
    }
  }
});

function addRecipient(): void {
  const id = linkRecipientId.value;
  if (id == null) {
    return;
  }
  if (recipients.value.some((recipient) => recipient.id === id)) {
    showError(new ApiError("GraveRecipientLinkExists"));
    return;
  }
  const name =
    allRecipients.data.value?.find((recipient) => recipient.Id === id)?.Name ?? String(id);
  recipients.value = [...recipients.value, { id, name }];
}

function removeRecipient(id: number): void {
  recipients.value = recipients.value.filter((recipient) => recipient.id !== id);
}

const items = useItemsQuery(ref(""));
const defaultPrices = computed(
  () => new Map((items.data.value ?? []).map((item) => [item.Id, item.DefaultPrice])),
);
// Every grave needs an item billed all year, so that every half-year of its
// term bills something. Reported only once an item is picked: a blank editor
// has not got anything wrong yet.
const lacksAllYearItem = computed(() => {
  const picked = lines.value.filter((line) => line.itemId != null);
  if (picked.length === 0) {
    return false;
  }
  const quarters = new Map((items.data.value ?? []).map((item) => [item.Id, item.Quarter]));
  return !picked.some((line) => quarters.get(line.itemId as number) == null);
});
const previewInput = computed<PreviewInput | null>(() => {
  if (!visible.value || loading.value) {
    return null;
  }
  // an end before the start is the save's to refuse; a preview of it would
  // only read as the check being down
  if (form.StartDate && form.EndDate && form.EndDate.getTime() < form.StartDate.getTime()) {
    return null;
  }
  const { inputs, keys } = previewLines(
    lines.value,
    (itemId) => defaultPrices.value.get(itemId) ?? null,
  );
  if (inputs.length === 0) {
    return null;
  }
  return {
    recipients: Math.max(1, recipients.value.length),
    startMonth: toIsoMonth(form.StartDate),
    endMonth: toIsoMonth(form.EndDate),
    lines: inputs,
    keys,
  };
});
const billing = useGraveBillingPreview(previewInput);
const suggestions = computed(() => {
  const map = new Map<number, number[]>();
  const keys = billing.answered.value?.keys ?? [];
  for (const entry of billing.preview.value?.LineSuggestions ?? []) {
    const key = keys[entry.LineIndex];
    if (key != null) {
      map.set(key, entry.Suggestions);
    }
  }
  return map;
});

/** Moves focus to the first invoice that does not divide, once it is drawn. */
async function focusFailingInvoice(): Promise<void> {
  await nextTick();
  formEl.value?.querySelector<HTMLElement>("[data-billing-invalid]")?.focus();
}

async function save(): Promise<void> {
  let inputs: InvoiceLineInput[];
  try {
    inputs = toLineInputs(lines.value);
  } catch (error) {
    showError(error);
    return;
  }
  if (lacksAllYearItem.value || !lines.value.some((line) => line.itemId != null)) {
    showError(new ApiError("AllYearItemRequired"));
    return;
  }
  saving.value = true;
  try {
    if (previewInput.value) {
      const check = await billing.check();
      if (check && !check.Ok) {
        showError(new ApiError("PricesDoNotDivide"));
        await focusFailingInvoice();
        return;
      }
    }
    await saveGrave({
      graveId: props.graveId,
      graveNumber: form.GraveNumber,
      fieldNumber: form.FieldNumber,
      siteName: form.SiteName,
      position: form.Position,
      startDate: toIsoMonth(form.StartDate),
      endDate: toIsoMonth(form.EndDate),
      items: inputs,
      recipientIds: recipients.value.map((recipient) => recipient.id),
    });
    await queryClient.invalidateQueries();
    visible.value = false;
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <Drawer
    v-model:visible="visible"
    position="right"
    :header="graveId == null ? t('createGrave') : t('editGrave')"
    :aria-label="graveId == null ? t('createGrave') : t('editGrave')"
    :style="{ width: 'min(100vw, 60rem)' }"
    @after-show="drawerFocus.focusFirst(formEl)"
    @after-hide="drawerFocus.restore()"
    @keydown.capture="drawerFocus.onKeydown($event, () => (visible = false))"
  >
    <div v-if="loading" class="flex justify-center p-10"><ProgressSpinner /></div>
    <form ref="formEl" v-else id="graveForm" class="flex flex-col gap-6" @submit.prevent="save">
      <Message v-if="graveId != null" severity="warn">{{ t("graveEditWarning") }}</Message>

      <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <div class="flex flex-col gap-2">
          <label for="graveNumber" class="font-medium">{{ t("graveNumber") }}</label>
          <InputText id="graveNumber" v-model="form.GraveNumber" />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveFieldNumber" class="font-medium">{{ t("graveFieldNumber") }}</label>
          <InputText id="graveFieldNumber" v-model="form.FieldNumber" />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveSiteName" class="font-medium">{{ t("graveSiteName") }}</label>
          <InputText id="graveSiteName" v-model="form.SiteName" />
        </div>
        <div class="flex flex-col gap-2">
          <label for="gravePosition" class="font-medium">{{ t("gravePosition") }}</label>
          <Select
            v-model="form.Position"
            input-id="gravePosition"
            :options="positionOptions"
            option-label="label"
            option-value="value"
            fluid
          />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveStart" class="font-medium">{{ t("startDate") }}</label>
          <DatePicker
            v-model="form.StartDate"
            input-id="graveStart"
            view="month"
            date-format="mm/yy"
            show-icon
            fluid
          />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveEnd" class="font-medium">{{ t("endDate") }}</label>
          <DatePicker
            v-model="form.EndDate"
            input-id="graveEnd"
            view="month"
            date-format="mm/yy"
            show-icon
            show-clear
            fluid
          />
        </div>
      </div>

      <section class="flex flex-col gap-2">
        <h3 class="m-0 text-base font-semibold">{{ t("graveItems") }}</h3>
        <LineEditor v-model="lines" :suggestions="suggestions" />
        <Message v-if="lacksAllYearItem" severity="warn" size="small">{{
          t("allYearItemRequired")
        }}</Message>
      </section>

      <GraveBillingPreview
        :preview="billing.preview.value"
        :pending="billing.pending.value"
        :failed="billing.failed.value"
        :assumes-one-recipient="recipients.length === 0"
      />

      <section class="flex flex-col gap-3">
        <h3 class="m-0 text-base font-semibold">{{ t("graveRecipients") }}</h3>
        <Message v-if="graveId != null" severity="info" size="small">
          {{ t("graveRecipientsFixed") }}
        </Message>
        <ul class="m-0 flex list-none flex-col gap-1 p-0">
          <li
            v-for="recipient in recipients"
            :key="recipient.id"
            class="flex min-h-10 items-center justify-between rounded-lg border border-surface px-3 py-1"
          >
            <span>{{ recipient.name }}</span>
            <Button
              v-if="graveId == null"
              icon="pi pi-times"
              text
              rounded
              severity="danger"
              :aria-label="t('delete')"
              @click="removeRecipient(recipient.id)"
            />
          </li>
        </ul>
        <div
          v-if="graveId == null && recipients.length < MAX_RECIPIENTS"
          class="flex flex-wrap items-center gap-2"
        >
          <RecipientPicker
            v-model="linkRecipientId"
            input-id="graveRecipient"
            default-to-newest
            class="min-w-0 flex-1"
          />
          <Button :label="t('add')" icon="pi pi-plus" outlined @click="addRecipient" />
        </div>
        <Message
          v-if="graveId == null && recipients.length >= MAX_RECIPIENTS"
          severity="info"
          size="small"
        >
          {{ t("graveRecipientsMax") }}
        </Message>
      </section>
    </form>
    <template #footer>
      <div class="flex justify-end gap-2">
        <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
        <Button
          type="submit"
          form="graveForm"
          :label="t('save')"
          icon="pi pi-check"
          :loading="saving"
          :disabled="loading"
        />
      </div>
    </template>
  </Drawer>
</template>
