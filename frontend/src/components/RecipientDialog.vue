<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import InputText from "primevue/inputtext";
import Textarea from "primevue/textarea";
import { useToast } from "primevue/usetoast";
import { reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { createRecipient } from "@/api/endpoints";
import type { Recipient } from "@/api/generated";
import { odata } from "@/api/odata";
import { useErrorToast } from "@/composables/useErrorToast";

const props = defineProps<{ recipient: Recipient | null }>();
const visible = defineModel<boolean>("visible", { required: true });

const { t } = useI18n();
const { showError } = useErrorToast();
const toast = useToast();
const queryClient = useQueryClient();
const form = reactive({ Number: "", Name: "", Address: "", Email: "" });

watch(visible, (open) => {
  if (open) {
    Object.assign(form, {
      Number: props.recipient?.Number ?? "",
      Name: props.recipient?.Name ?? "",
      Address: props.recipient?.Address ?? "",
      Email: props.recipient?.Email ?? "",
    });
  }
});

const saving = ref(false);
async function save(): Promise<void> {
  saving.value = true;
  const number = form.Number.trim();
  // an empty address is none; the backend stores it as null either way
  const email = form.Email.trim() || null;
  try {
    if (props.recipient) {
      await odata
        .Recipients(props.recipient.Id)
        .patch({
          Number: number,
          Name: form.Name.trim(),
          Address: form.Address.trim(),
          Email: email,
        })
        .execute();
    } else {
      // an empty number is left out: the backend assigns the next one
      await createRecipient({
        Name: form.Name.trim(),
        Address: form.Address.trim(),
        Email: email,
        ...(number ? { Number: number } : {}),
      });
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
    :header="recipient ? t('editRecipient') : t('createRecipient')"
    :style="{ width: '28rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <form id="recipientForm" class="flex flex-col gap-4" @submit.prevent="save">
      <div class="flex flex-col gap-2">
        <label for="recipientNumber" class="font-medium">{{ t("recipientNumber") }}</label>
        <InputText
          id="recipientNumber"
          v-model="form.Number"
          :placeholder="recipient ? undefined : t('recipientNumberHint')"
        />
      </div>
      <div class="flex flex-col gap-2">
        <label for="recipientName" class="font-medium">{{ t("recipientName") }}</label>
        <InputText id="recipientName" v-model="form.Name" autofocus />
      </div>
      <div class="flex flex-col gap-2">
        <label for="recipientAddress" class="font-medium">{{ t("recipientAddress") }}</label>
        <Textarea id="recipientAddress" v-model="form.Address" rows="4" />
      </div>
      <div class="flex flex-col gap-2">
        <label for="recipientEmail" class="font-medium">{{ t("recipientEmail") }}</label>
        <InputText
          id="recipientEmail"
          v-model="form.Email"
          inputmode="email"
          :placeholder="t('recipientEmailHint')"
        />
      </div>
    </form>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        type="submit"
        form="recipientForm"
        :label="t('save')"
        icon="pi pi-check"
        :loading="saving"
      />
    </template>
  </Dialog>
</template>
