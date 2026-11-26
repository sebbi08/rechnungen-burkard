<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Drawer from "primevue/drawer";
import Message from "primevue/message";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import {
  checkGraveBilling,
  createGraveInvoiceRevision,
  type InvoiceLineInput,
} from "@/api/endpoints";
import { useBlocking } from "@/composables/useBlocking";
import { useDiscardGuard } from "@/composables/useDiscardGuard";
import { useDrawerFocus } from "@/composables/useDrawerFocus";
import { useErrorToast } from "@/composables/useErrorToast";
import type { GraveInvoiceDraft } from "@/composables/useGraveInvoiceEdit";
import { showMessage } from "@/composables/useMessage";
import { billingProblemText } from "@/lib/billing";
import { formatRecipientLabel, type Translate } from "@/lib/format";
import { type DraftLine, toLineInputs } from "@/lib/lines";
import LineEditor from "./LineEditor.vue";
import RecipientCard from "./RecipientCard.vue";
import RecipientPicker from "./RecipientPicker.vue";

const props = defineProps<{ draft: GraveInvoiceDraft | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const translate: Translate = (key, list) => (list ? t(key, list) : t(key));
const { showError } = useErrorToast();
const drawerFocus = useDrawerFocus();
const formEl = ref<HTMLFormElement | null>(null);
const { run } = useBlocking();
const queryClient = useQueryClient();
const lines = ref<DraftLine[]>([]);
// Who each recipient invoice goes to now: the member's own recipient until the
// picker is used.
const memberRecipients = ref<(number | null)[]>([]);
const discard = useDiscardGuard(visible, () => ({
  lines: lines.value.map(({ itemId, count, unitPrice }) => ({ itemId, count, unitPrice })),
  recipients: memberRecipients.value,
}));

/** The ids the other members hold, which a member's picker does not offer. */
function othersOf(index: number): number[] {
  return memberRecipients.value.filter((id, i): id is number => i !== index && id != null);
}

watch(visible, (open) => {
  if (open) {
    drawerFocus.remember();
    lines.value = props.draft?.lines ?? [];
    memberRecipients.value = (props.draft?.members ?? []).map((member) => member.recipientId);
    discard.markClean();
  }
});

async function save(): Promise<void> {
  const draft = props.draft;
  // a click landing while the drawer slides out after a save
  if (!draft || !visible.value) {
    return;
  }
  let inputs: InvoiceLineInput[];
  try {
    inputs = toLineInputs(lines.value);
  } catch (error) {
    showError(error);
    return;
  }
  // a member is replaced once its picker holds another recipient
  const replacements = draft.members.flatMap((member, index) => {
    const recipientId = memberRecipients.value[index];
    return recipientId != null && recipientId !== member.recipientId
      ? [{ invoiceId: member.invoiceId, recipientId }]
      : [];
  });
  try {
    // The backend refuses prices that do not split across the recipients, but
    // only by name; the check first lets the message say which rate and what
    // would work, as the grave editor does. Under the overlay with the save and
    // the refetch, so a second Save cannot slip in (InvoiceEditor).
    const problems = await run(t("invoiceRunning"), async () => {
      // a check that cannot be reached does not block: the save validates again
      const check = await checkGraveBilling(draft.recipientCount, inputs).catch(() => null);
      if (check && !check.Ok) {
        return billingProblemText(check.Problems, draft.recipientCount, translate);
      }
      await createGraveInvoiceRevision(draft.invoiceId, inputs, replacements);
      await queryClient.invalidateQueries();
      return null;
    });
    if (problems) {
      showMessage(t("pricesDoNotDivideTitle"), problems);
      return;
    }
    visible.value = false;
  } catch (error) {
    showError(error);
  }
}
</script>

<template>
  <Drawer
    :visible="visible"
    :close-on-escape="false"
    position="right"
    :header="t('graveInvoiceEditTitle')"
    :aria-label="t('graveInvoiceEditTitle')"
    :style="{ width: 'min(100vw, 60rem)' }"
    @after-show="drawerFocus.focusFirst(formEl)"
    @after-hide="drawerFocus.restore()"
    @update:visible="discard.onVisibleChange"
    @keydown.capture="drawerFocus.onKeydown($event, discard.requestClose)"
  >
    <form ref="formEl" id="graveInvoiceForm" class="flex flex-col gap-6" @submit.prevent="save">
      <Message v-if="draft" severity="warn">{{ draft.warning }}</Message>
      <section v-if="draft" class="flex flex-col gap-3">
        <h3 class="m-0 text-base font-semibold">{{ t("graveInvoiceMembers") }}</h3>
        <div
          v-for="(member, index) in draft.members"
          :key="member.invoiceId"
          class="flex flex-col gap-2 rounded-lg border border-surface p-3"
        >
          <RecipientCard :number="member.number" :name="member.name" :address="member.address" />
          <RecipientPicker
            v-model="memberRecipients[index]"
            :input-id="`graveInvoiceMember${index}`"
            :aria-label="
              t('graveInvoiceMemberPicker', [formatRecipientLabel(member.number, member.name)])
            "
            :exclude="othersOf(index)"
          />
        </div>
      </section>
      <LineEditor v-model="lines" />
    </form>
    <template #footer>
      <div class="flex justify-end gap-2">
        <Button :label="t('cancel')" severity="secondary" text @click="discard.requestClose" />
        <Button type="submit" form="graveInvoiceForm" :label="t('save')" icon="pi pi-check" />
      </div>
    </template>
  </Drawer>
</template>
