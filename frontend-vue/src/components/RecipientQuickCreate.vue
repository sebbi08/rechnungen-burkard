<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import InputText from "primevue/inputtext";
import Textarea from "primevue/textarea";
import { reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { createRecipient } from "@/api/endpoints";
import { useErrorToast } from "@/composables/useErrorToast";

const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ created: [id: number] }>();

const { t } = useI18n();
const { showError } = useErrorToast();
const queryClient = useQueryClient();
const form = reactive({ Name: "", Address: "" });

watch(visible, (open) => {
  if (open) {
    Object.assign(form, { Name: "", Address: "" });
  }
});

const saving = ref(false);
async function save(): Promise<void> {
  saving.value = true;
  try {
    const id = await createRecipient({ Name: form.Name, Address: form.Address });
    await queryClient.invalidateQueries({ queryKey: ["recipients"] });
    emit("created", id);
    visible.value = false;
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
    :header="t('newRecipient')"
    :style="{ width: '26rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <form id="quickRecipientForm" class="flex flex-col gap-4" @submit.prevent="save">
      <div class="flex flex-col gap-2">
        <label for="quickName" class="font-medium">{{ t("recipientName") }}</label>
        <InputText id="quickName" v-model="form.Name" autofocus />
      </div>
      <div class="flex flex-col gap-2">
        <label for="quickAddress" class="font-medium">{{ t("recipientAddress") }}</label>
        <Textarea id="quickAddress" v-model="form.Address" rows="4" />
      </div>
    </form>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        type="submit"
        form="quickRecipientForm"
        :label="t('save')"
        icon="pi pi-check"
        :loading="saving"
      />
    </template>
  </Dialog>
</template>
