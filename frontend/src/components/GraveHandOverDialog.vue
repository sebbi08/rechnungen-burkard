<script setup lang="ts">
import Button from "primevue/button";
import DatePicker from "primevue/datepicker";
import Dialog from "primevue/dialog";
import { ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { currentHalfYear } from "@/lib/halfYear";

const visible = defineModel<boolean>("visible", { required: true });
const emit = defineEmits<{ confirm: [month: Date] }>();

const { t } = useI18n();
const month = ref<Date | null>(null);

// The first month of the next half-year: the current one may already be
// invoiced, which the server refuses a hand-over inside.
watch(visible, (open) => {
  if (open) {
    const [year, half] = currentHalfYear();
    month.value = half === 1 ? new Date(year, 6, 1) : new Date(year + 1, 0, 1);
  }
});
</script>

<template>
  <Dialog
    v-model:visible="visible"
    modal
    :header="t('handOverTitle')"
    :style="{ width: '28rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <form
      id="handOverForm"
      class="flex flex-col gap-3"
      @submit.prevent="month && emit('confirm', month)"
    >
      <p class="m-0">{{ t("handOverIntro") }}</p>
      <div class="flex flex-col gap-2">
        <label for="handOverMonth" class="font-medium">{{ t("handOverMonth") }}</label>
        <DatePicker
          v-model="month"
          input-id="handOverMonth"
          view="month"
          date-format="mm/yy"
          show-icon
          fluid
        />
      </div>
    </form>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="visible = false" />
      <Button
        type="submit"
        form="handOverForm"
        :label="t('handOverContinue')"
        icon="pi pi-arrow-right"
        :disabled="month == null"
      />
    </template>
  </Dialog>
</template>
