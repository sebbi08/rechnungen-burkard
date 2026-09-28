<script setup lang="ts">
import Button from "primevue/button";
import Dialog from "primevue/dialog";
import { useI18n } from "vue-i18n";
import { messageState } from "@/composables/useMessage";

const { t } = useI18n();
</script>

<template>
  <Dialog
    v-model:visible="messageState.visible"
    modal
    :header="messageState.title"
    :style="{ width: '34rem' }"
    :breakpoints="{ '640px': '95vw' }"
  >
    <div class="flex gap-3">
      <i
        :class="
          messageState.severity === 'error'
            ? 'pi pi-times-circle text-red-500'
            : 'pi pi-info-circle text-primary'
        "
        class="mt-0.5 text-xl"
        aria-hidden="true"
      />
      <p class="m-0 whitespace-pre-line">{{ messageState.text }}</p>
    </div>
    <template #footer>
      <Button :label="t('ok')" autofocus @click="messageState.visible = false" />
    </template>
  </Dialog>
</template>
