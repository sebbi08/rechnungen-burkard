<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import DatePicker from "primevue/datepicker";
import Drawer from "primevue/drawer";
import InputText from "primevue/inputtext";
import Message from "primevue/message";
import ProgressSpinner from "primevue/progressspinner";
import { reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import {
  checkGraveBilling,
  fetchGraveDetails,
  type GraveBillingCheck,
  type InvoiceLineInput,
  saveGrave,
} from "@/api/endpoints";
import { ApiError } from "@/api/http";
import { useRecipientsQuery } from "@/api/queries";
import { useErrorToast } from "@/composables/useErrorToast";
import { showMessage } from "@/composables/useMessage";
import { billingProblemText } from "@/lib/billing";
import { parseIsoDate, toIsoDate, type Translate } from "@/lib/format";
import { type DraftLine, newLine, toLineInputs } from "@/lib/lines";
import LineEditor from "./LineEditor.vue";
import RecipientPicker from "./RecipientPicker.vue";

const props = defineProps<{ graveId: number | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const translate: Translate = (key, list) => (list ? t(key, list) : t(key));
const { showError } = useErrorToast();
const queryClient = useQueryClient();
const allRecipients = useRecipientsQuery(ref(""));

const form = reactive({
  GraveNumber: "",
  StartDate: null as Date | null,
  EndDate: null as Date | null,
});
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
  if (!open) {
    loading.value = false;
    return;
  }
  Object.assign(form, { GraveNumber: "", StartDate: null, EndDate: null });
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

/** The bill is split equally, so each VAT rate has to divide. Asking before
 *  the save turns a refusal into a message that names the rate and what would
 *  work. A check that cannot be reached does not block: the save validates
 *  again. */
async function billingProblems(inputs: InvoiceLineInput[]): Promise<string | null> {
  const count = recipients.value.length;
  if (count === 0 || inputs.length === 0) {
    return null;
  }
  let check: GraveBillingCheck;
  try {
    check = await checkGraveBilling(count, inputs);
  } catch {
    return null;
  }
  return check.Ok ? null : billingProblemText(check.Problems, count, translate);
}

async function save(): Promise<void> {
  let inputs: InvoiceLineInput[];
  try {
    inputs = toLineInputs(lines.value);
  } catch (error) {
    showError(error);
    return;
  }
  saving.value = true;
  try {
    const problems = await billingProblems(inputs);
    if (problems) {
      showMessage(t("pricesDoNotDivideTitle"), problems);
      return;
    }
    await saveGrave({
      graveId: props.graveId,
      graveNumber: form.GraveNumber,
      // the local calendar day, not toISOString (which is the day before here)
      startDate: toIsoDate(form.StartDate),
      endDate: toIsoDate(form.EndDate),
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
    :style="{ width: 'min(100vw, 60rem)' }"
  >
    <div v-if="loading" class="flex justify-center p-10"><ProgressSpinner /></div>
    <form v-else id="graveForm" class="flex flex-col gap-6" @submit.prevent="save">
      <Message v-if="graveId != null" severity="warn">{{ t("graveEditWarning") }}</Message>

      <div class="grid gap-4 sm:grid-cols-3">
        <div class="flex flex-col gap-2">
          <label for="graveNumber" class="font-medium">{{ t("graveNumber") }}</label>
          <InputText id="graveNumber" v-model="form.GraveNumber" />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveStart" class="font-medium">{{ t("startDate") }}</label>
          <DatePicker
            v-model="form.StartDate"
            input-id="graveStart"
            date-format="dd.mm.yy"
            show-icon
            fluid
          />
        </div>
        <div class="flex flex-col gap-2">
          <label for="graveEnd" class="font-medium">{{ t("endDate") }}</label>
          <DatePicker
            v-model="form.EndDate"
            input-id="graveEnd"
            date-format="dd.mm.yy"
            show-icon
            fluid
          />
        </div>
      </div>

      <section class="flex flex-col gap-2">
        <h3 class="m-0 text-base font-semibold">{{ t("graveItems") }}</h3>
        <LineEditor v-model="lines" />
      </section>

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
        <div v-if="graveId == null" class="flex flex-wrap items-center gap-2">
          <RecipientPicker
            v-model="linkRecipientId"
            input-id="graveRecipient"
            default-to-newest
            class="min-w-0 flex-1"
          />
          <Button :label="t('add')" icon="pi pi-plus" outlined @click="addRecipient" />
        </div>
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
