<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Checkbox from "primevue/checkbox";
import DatePicker from "primevue/datepicker";
import Drawer from "primevue/drawer";
import InputText from "primevue/inputtext";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import Select from "primevue/select";
import { computed, nextTick, reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import {
  type BillingCorrection,
  fetchGraveDetails,
  type GraveDetails,
  saveGrave,
} from "@/api/endpoints";
import type { Recipient } from "@/api/generated";
import { ApiError, toApiError } from "@/api/http";
import { useCemeteriesQuery, useItemsQuery } from "@/api/queries";
import { useDiscardGuard } from "@/composables/useDiscardGuard";
import { useDrawerFocus } from "@/composables/useDrawerFocus";
import { useErrorToast } from "@/composables/useErrorToast";
import { type PreviewInput, useGraveBillingPreview } from "@/composables/useGraveBillingPreview";
import { billingTitle, MAX_RECIPIENTS, previewLines } from "@/lib/billing";
import { parseIsoDate, toIsoMonth } from "@/lib/format";
import { type GravePositionName, OFFERED_GRAVE_POSITIONS } from "@/lib/gravePosition";
import { type DraftLine, newLine, toLineInputs } from "@/lib/lines";
import CorrectionsDialog, { type CorrectionRow } from "./CorrectionsDialog.vue";
import GraveBillingPreview from "./GraveBillingPreview.vue";
import GraveHandOverDialog from "./GraveHandOverDialog.vue";
import LineEditor from "./LineEditor.vue";
import RecipientPicker from "./RecipientPicker.vue";

const props = defineProps<{
  graveId: number | null;
  /** A new grave starts out as a cooperative one. */
  initialCooperative?: boolean;
  /** A new grave that takes over from this one, prefilled from it and of the
   *  other kind, starting in `successorStart`. */
  successorOf?: GraveDetails | null;
  successorStart?: Date | null;
}>();
const visible = defineModel<boolean>("visible", { required: true });
// The editor has closed (the user may have been asked to discard first) and
// the host opens the successor in its place.
const emit = defineEmits<{ handOver: [grave: GraveDetails, month: Date] }>();

const { t } = useI18n();
const { showError } = useErrorToast();
const drawerFocus = useDrawerFocus();
const formEl = ref<HTMLFormElement | null>(null);
const queryClient = useQueryClient();

const form = reactive({
  GraveNumber: "",
  FieldNumber: "",
  SiteName: "",
  CemeteryId: null as number | null,
  Position: null as GravePositionName | null,
  StartDate: null as Date | null,
  EndDate: null as Date | null,
  IsCooperative: false,
  ContractNumber: "",
});
// The position the grave was stored with. One the editor does not offer
// (imported from the old program) is listed as well, so that the grave can be
// saved unchanged; the save refuses it for any other grave.
const storedPosition = ref<GravePositionName | null>(null);
const positionOptions = computed(() => {
  const offered: GravePositionName[] = [...OFFERED_GRAVE_POSITIONS];
  const stored = storedPosition.value;
  return (stored && !offered.includes(stored) ? [...offered, stored] : offered).map((value) => ({
    value,
    label: t(`gravePosition${value}`),
  }));
});
const title = computed(() =>
  props.graveId != null
    ? t("editGrave")
    : props.successorOf
      ? t("createSuccessorGrave")
      : t("createGrave"),
);
const cemeteries = useCemeteriesQuery();
const lines = ref<DraftLine[]>([]);
const recipients = ref<{ id: number; name: string }[]>([]);
const linkRecipientId = ref<number | null>(null);
// the recipient behind that id, as the picker knows it
const linkRecipient = ref<Recipient | null>(null);
// The grave as stored; null for a new one.
const stored = ref<GraveDetails | null>(null);
const handOverVisible = ref(false);
const loading = ref(false);
const saving = ref(false);
// What the user can change; the recipient picked for adding is not, until it
// is added.
const discard = useDiscardGuard(visible, () => ({
  form,
  lines: lines.value.map(({ itemId, count, unitPrice, priceIncludesVat }) => ({
    itemId,
    count,
    unitPrice,
    priceIncludesVat,
  })),
  recipients: recipients.value.map((recipient) => recipient.id),
}));
// Which opening a load belongs to. An answer that arrives after the drawer was
// closed -- or reopened for another grave, or for a new one -- is dropped:
// filled in, it turned a new grave into a copy of the old one.
let opening = 0;

function fill(grave: GraveDetails): void {
  Object.assign(form, {
    GraveNumber: grave.GraveNumber,
    FieldNumber: grave.FieldNumber,
    SiteName: grave.SiteName,
    CemeteryId: grave.CemeteryId,
    Position: grave.Position,
    StartDate: parseIsoDate(grave.StartDate),
    EndDate: parseIsoDate(grave.EndDate),
    IsCooperative: grave.IsCooperative,
    ContractNumber: grave.ContractNumber ?? "",
  });
  storedPosition.value = grave.Position;
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
}

watch(visible, async (open) => {
  const current = ++opening;
  if (open) {
    drawerFocus.remember();
    // loaded with the page, which may be older than a cemetery added since;
    // the item and recipient pickers load anew as the drawer opens anyway
    void cemeteries.refetch();
  }
  if (!open) {
    loading.value = false;
    return;
  }
  Object.assign(form, {
    GraveNumber: "",
    FieldNumber: "",
    SiteName: "",
    CemeteryId: null,
    Position: null,
    StartDate: null,
    EndDate: null,
    IsCooperative: props.initialCooperative ?? false,
    ContractNumber: "",
  });
  stored.value = null;
  storedPosition.value = null;
  lines.value = [];
  recipients.value = [];
  linkRecipientId.value = null;
  if (props.graveId == null) {
    const predecessor = props.successorOf;
    if (predecessor) {
      fill(predecessor);
      Object.assign(form, {
        StartDate: props.successorStart ?? null,
        EndDate: null,
        IsCooperative: !predecessor.IsCooperative,
        // the predecessor's number is its own: a successor of an ordinary grave
        // is given one, and one handed back to the business has none
        ContractNumber: "",
      });
    }
    discard.markClean();
    return;
  }
  loading.value = true;
  try {
    const grave = await fetchGraveDetails(props.graveId);
    if (current !== opening) {
      return;
    }
    fill(grave);
    stored.value = grave;
    discard.markClean();
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
  const recipient = linkRecipient.value;
  if (recipient == null || recipient.Id !== linkRecipientId.value) {
    return;
  }
  recipients.value = [...recipients.value, { id: recipient.Id, name: recipient.Name }];
  // the picker no longer offers a recipient the grave has
  linkRecipientId.value = null;
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
  // a cooperative grave is not billed here, so nothing has to be billable
  if (form.IsCooperative) {
    return false;
  }
  const picked = lines.value.filter((line) => line.itemId != null);
  if (picked.length === 0) {
    return false;
  }
  const quarters = new Map((items.data.value ?? []).map((item) => [item.Id, item.Quarter]));
  return !picked.some((line) => quarters.get(line.itemId as number) == null);
});
const previewInput = computed<PreviewInput | null>(() => {
  if (!visible.value || loading.value || form.IsCooperative) {
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
// What the confirmation dialog lists, and what a confirm saves.
const correctionsVisible = ref(false);
const correctionRows = ref<CorrectionRow[]>([]);
const pendingCorrections = ref<BillingCorrection[]>([]);

/** Moves focus to the first invoice that does not divide, once it is drawn. */
async function focusFailingInvoice(): Promise<void> {
  await nextTick();
  formEl.value?.querySelector<HTMLElement>("[data-billing-invalid]")?.focus();
}

// The box is fixed once invoices exist: they were split by who bills them.
const flagLocked = computed(() => stored.value?.HasInvoices === true || props.successorOf != null);
// The recipients of a grave can change until its first invoice: the invoices
// were split among them.
const recipientsEditable = computed(() => props.graveId == null || !stored.value?.HasInvoices);

async function save(): Promise<void> {
  if (form.CemeteryId == null) {
    showError(new ApiError("CemeteryRequired"));
    return;
  }
  if (form.IsCooperative && form.ContractNumber.trim() === "") {
    showError(new ApiError("ContractNumberRequired"));
    return;
  }
  try {
    toLineInputs(lines.value);
  } catch (error) {
    showError(error);
    return;
  }
  if (
    !form.IsCooperative &&
    (lacksAllYearItem.value || !lines.value.some((line) => line.itemId != null))
  ) {
    showError(new ApiError("AllYearItemRequired"));
    return;
  }

  saving.value = true;
  try {
    const corrections: BillingCorrection[] = [];
    if (previewInput.value) {
      const check = await billing.check();
      if (check && !check.Ok) {
        showError(new ApiError("PricesDoNotDivide"));
        await focusFailingInvoice();
        return;
      }
      const rows: CorrectionRow[] = [];
      for (const invoice of check?.Invoices ?? []) {
        for (const correction of invoice.Corrections) {
          const line = invoice.Lines.find((entry) => entry.LineIndex === correction.LineIndex);
          rows.push({
            invoice: billingTitle(invoice, t),
            item: `${correction.ItemNumber} ${line?.Description ?? ""}`.trim(),
            vatRate: correction.VatRate,
            entered: correction.EnteredPrice,
            corrected: correction.CorrectedPrice,
          });
          corrections.push(correction);
        }
      }
      if (corrections.length > 0) {
        // saved only once the user confirms what will be raised
        correctionRows.value = rows;
        pendingCorrections.value = corrections;
        correctionsVisible.value = true;
        return;
      }
    }
    await persist(corrections);
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}

async function confirmCorrections(): Promise<void> {
  if (saving.value) {
    return;
  }
  saving.value = true;
  try {
    await persist(pendingCorrections.value);
    correctionsVisible.value = false;
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}

// Only a confirm may send the corrections it was shown.
watch(correctionsVisible, (open) => {
  if (!open) {
    correctionRows.value = [];
    pendingCorrections.value = [];
  }
});

// Asks to discard what was typed first; the host opens the successor once the
// editor has closed.
async function handOver(month: Date): Promise<void> {
  const grave = stored.value;
  handOverVisible.value = false;
  if (grave == null) {
    return;
  }
  await discard.requestClose();
  if (!visible.value) {
    emit("handOver", grave, month);
  }
}

async function persist(corrections: BillingCorrection[]): Promise<void> {
  try {
    await saveGrave({
      graveId: props.graveId,
      graveNumber: form.GraveNumber,
      fieldNumber: form.FieldNumber,
      siteName: form.SiteName,
      cemeteryId: form.CemeteryId,
      position: form.Position,
      startDate: toIsoMonth(form.StartDate),
      endDate: toIsoMonth(form.EndDate),
      items: toLineInputs(lines.value),
      recipientIds: recipients.value.map((recipient) => recipient.id),
      isCooperative: form.IsCooperative,
      contractNumber: form.IsCooperative ? form.ContractNumber.trim() : null,
      succeedsGraveId: props.graveId == null ? (props.successorOf?.Id ?? null) : null,
      corrections: corrections.map((c) => ({
        kind: c.Kind,
        year: c.Year,
        half: c.Half,
        vatRate: c.VatRate,
        itemId: c.ItemId,
        correctedPrice: c.CorrectedPrice,
      })),
    });
  } catch (error) {
    if (toApiError(error).code !== "CorrectionsChanged") {
      throw error;
    }
    // the corrections the save computes differ from the ones sent: look again
    correctionsVisible.value = false;
    await billing.check();
    showError(error);
    return;
  }
  await queryClient.invalidateQueries();
  visible.value = false;
}
</script>

<template>
  <Drawer
    :visible="visible"
    :close-on-escape="false"
    position="right"
    :header="title"
    :aria-label="title"
    :style="{ width: 'min(100vw, 60rem)' }"
    @after-show="drawerFocus.focusFirst(formEl)"
    @after-hide="drawerFocus.restore()"
    @update:visible="discard.onVisibleChange"
    @keydown.capture="drawerFocus.onKeydown($event, discard.requestClose)"
  >
    <div v-if="loading" class="flex justify-center p-10"><ProgressSpinner /></div>
    <form ref="formEl" v-else id="graveForm" class="flex flex-col gap-6" @submit.prevent="save">
      <Message v-if="graveId != null" severity="warn">{{ t("graveEditWarning") }}</Message>
      <Message v-if="successorOf" severity="info">
        {{ t("graveSuccessorInfo", [successorOf.GraveNumber]) }}
      </Message>

      <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <div class="flex flex-col gap-2">
          <label for="graveNumber" class="font-medium">{{ t("graveNumber") }}</label>
          <InputText id="graveNumber" v-model="form.GraveNumber" :disabled="successorOf != null" />
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
          <label for="graveCemetery" class="font-medium">{{ t("graveCemetery") }}</label>
          <!-- the settings keep a handful, so all of them are offered and
               searched here rather than by the server -->
          <Select
            v-model="form.CemeteryId"
            input-id="graveCemetery"
            :options="cemeteries.data.value ?? []"
            option-label="Name"
            option-value="Id"
            filter
            :placeholder="t('selectCemetery')"
            :loading="cemeteries.isPending.value"
            fluid
            overlay-class="max-w-[calc(100vw-1rem)]"
          />
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
        <div v-if="form.IsCooperative" class="flex flex-col gap-2">
          <label for="graveContractNumber" class="font-medium">{{ t("contractNumber") }}</label>
          <InputText id="graveContractNumber" v-model="form.ContractNumber" />
        </div>
        <div class="flex flex-col gap-2 sm:col-span-2 lg:col-span-3">
          <div class="flex items-center gap-2">
            <Checkbox
              v-model="form.IsCooperative"
              input-id="graveCooperative"
              binary
              :disabled="flagLocked"
            />
            <label for="graveCooperative" class="font-medium">{{ t("cooperativeGrave") }}</label>
          </div>
          <Message v-if="stored?.HasInvoices" severity="info" size="small">
            {{ t("cooperativeFlagLocked") }}
          </Message>
        </div>
      </div>

      <section class="flex flex-col gap-2">
        <h3 class="m-0 text-base font-semibold">{{ t("graveItems") }}</h3>
        <LineEditor v-model="lines" />
        <Message v-if="lacksAllYearItem" severity="warn" size="small">{{
          t("allYearItemRequired")
        }}</Message>
      </section>

      <GraveBillingPreview
        v-if="!form.IsCooperative"
        :preview="billing.preview.value"
        :pending="billing.pending.value"
        :failed="billing.failed.value"
        :assumes-one-recipient="recipients.length === 0"
      />

      <section class="flex flex-col gap-3">
        <h3 class="m-0 text-base font-semibold">{{ t("graveRecipients") }}</h3>
        <Message v-if="!recipientsEditable" severity="info" size="small">
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
              v-if="recipientsEditable"
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
          v-if="recipientsEditable && recipients.length < MAX_RECIPIENTS"
          class="flex flex-wrap items-center gap-2"
        >
          <!-- the newest recipient by default for the first one only: after an
               add the field stays empty rather than move on to the next -->
          <RecipientPicker
            v-model="linkRecipientId"
            input-id="graveRecipient"
            :default-to-newest="recipients.length === 0"
            :exclude="recipients.map((recipient) => recipient.id)"
            class="min-w-0 flex-1"
            @selected="linkRecipient = $event"
          />
          <Button :label="t('add')" icon="pi pi-plus" outlined @click="addRecipient" />
        </div>
        <Message
          v-if="recipientsEditable && recipients.length >= MAX_RECIPIENTS"
          severity="info"
          size="small"
        >
          {{ t("graveRecipientsMax") }}
        </Message>
      </section>
    </form>
    <template #footer>
      <div class="flex flex-wrap justify-end gap-2">
        <Button
          v-if="stored"
          :label="stored.IsCooperative ? t('handOverToBusiness') : t('handOverToCooperative')"
          icon="pi pi-arrow-right-arrow-left"
          severity="secondary"
          outlined
          class="mr-auto"
          :disabled="saving"
          @click="handOverVisible = true"
        />
        <Button
          :label="t('cancel')"
          severity="secondary"
          text
          :disabled="saving"
          @click="discard.requestClose"
        />
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
  <GraveHandOverDialog v-model:visible="handOverVisible" @confirm="handOver" />
  <CorrectionsDialog
    v-model:visible="correctionsVisible"
    :rows="correctionRows"
    :saving="saving"
    @confirm="confirmCorrections"
  />
</template>
