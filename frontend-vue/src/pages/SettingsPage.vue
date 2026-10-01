<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import InputNumber from "primevue/inputnumber";
import InputText from "primevue/inputtext";
import Select from "primevue/select";
import Textarea from "primevue/textarea";
import { useToast } from "primevue/usetoast";
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { onBeforeRouteLeave } from "vue-router";
import { ApiError } from "@/api/http";
import { odata } from "@/api/odata";
import { useSettingsQuery, useVatRatesQuery } from "@/api/queries";
import PageHeader from "@/components/PageHeader.vue";
import { useConfirmAsync } from "@/composables/useConfirmAsync";
import { useErrorToast } from "@/composables/useErrorToast";
import { vDecimalPoint } from "@/directives/decimalPoint";
import { formatVatRate } from "@/lib/format";

const { t } = useI18n();
const toast = useToast();
const { showError } = useErrorToast();
const ask = useConfirmAsync();
const queryClient = useQueryClient();

const settings = useSettingsQuery();
const form = reactive({ StampWord: "", EndingText: "", Language: "de" });
watch(
  () => settings.data.value,
  (value) => {
    if (value) {
      Object.assign(form, {
        StampWord: value.StampWord,
        EndingText: value.EndingText,
        Language: value.Language,
      });
    }
  },
  { immediate: true },
);

// Leaving with unsaved edits asks first -- inside the app, and on a reload or a
// closed tab through the browser's own prompt.
const dirty = computed(() => {
  const stored = settings.data.value;
  return (
    !!stored &&
    (form.StampWord !== stored.StampWord ||
      form.EndingText !== stored.EndingText ||
      form.Language !== stored.Language)
  );
});
onBeforeRouteLeave(async () => !dirty.value || (await ask(t("confirmDiscardSettings"))));
function onBeforeUnload(event: BeforeUnloadEvent): void {
  if (dirty.value) {
    event.preventDefault();
  }
}
onMounted(() => window.addEventListener("beforeunload", onBeforeUnload));
onBeforeUnmount(() => window.removeEventListener("beforeunload", onBeforeUnload));

const languages = computed(() => [
  { value: "de", label: t("languageDe") },
  { value: "en", label: t("languageEn") },
]);

const saving = ref(false);
async function save(): Promise<void> {
  saving.value = true;
  try {
    // no client check: the backend answers StampWordRequired / LanguageNotSupported
    await odata
      .Settings()
      .patch({ ...form })
      .execute();
    await queryClient.invalidateQueries();
    toast.add({ severity: "success", summary: t("saved"), life: 3000 });
  } catch (error) {
    showError(error);
  } finally {
    saving.value = false;
  }
}

const vatRates = useVatRatesQuery();
const newRate = ref<number | null>(null);

async function addVatRate(): Promise<void> {
  try {
    if (newRate.value == null || !(newRate.value > 0 && newRate.value < 100)) {
      throw new ApiError("InvalidVatRate");
    }
    await odata.VatRates().create({ Rate: newRate.value }).execute();
    newRate.value = null;
    await queryClient.invalidateQueries();
  } catch (error) {
    showError(error);
  }
}

async function deleteVatRate(id: number, rate: number): Promise<void> {
  if (!(await ask(t("confirmDeleteVatRate", [formatVatRate(rate)])))) {
    return;
  }
  try {
    await odata.VatRates(id).delete().execute();
    await queryClient.invalidateQueries();
  } catch (error) {
    showError(error);
  }
}
</script>

<template>
  <PageHeader :title="t('menuSettings')" />

  <div class="grid gap-6 lg:grid-cols-[2fr_1fr]">
    <Card>
      <template #title>{{ t("settingsDocumentsTitle") }}</template>
      <template #content>
        <form class="flex flex-col gap-5" @submit.prevent="save">
          <div class="flex flex-col gap-2">
            <label for="stampWord" class="font-medium">{{ t("settingsStampWord") }}</label>
            <InputText id="stampWord" v-model="form.StampWord" class="w-full md:w-80" />
          </div>
          <div class="flex flex-col gap-2">
            <label for="endingText" class="font-medium">{{ t("settingsEndingText") }}</label>
            <Textarea id="endingText" v-model="form.EndingText" rows="6" class="w-full" />
          </div>
          <div class="flex flex-col gap-2">
            <label for="language" class="font-medium">{{ t("settingsDocumentLanguage") }}</label>
            <Select
              v-model="form.Language"
              input-id="language"
              :options="languages"
              option-label="label"
              option-value="value"
              class="w-full md:w-80"
            />
          </div>
          <div>
            <Button type="submit" :label="t('save')" icon="pi pi-check" :loading="saving" />
          </div>
        </form>
      </template>
    </Card>

    <Card>
      <template #title>{{ t("settingsVatRates") }}</template>
      <template #content>
        <DataTable
          :value="vatRates.data.value ?? []"
          data-key="Id"
          size="small"
          :loading="vatRates.isFetching.value"
        >
          <template #empty>{{ t("noData") }}</template>
          <Column :header="t('vatRate')">
            <template #body="{ data }">{{ formatVatRate(data.Rate) }} %</template>
          </Column>
          <Column class="w-16 text-right">
            <template #body="{ data }">
              <Button
                v-tooltip.left="t('delete')"
                icon="pi pi-trash"
                severity="danger"
                text
                rounded
                :aria-label="t('delete')"
                @click="deleteVatRate(data.Id, data.Rate)"
              />
            </template>
          </Column>
        </DataTable>
        <form class="mt-4 flex flex-wrap items-end gap-2" @submit.prevent="addVatRate">
          <div class="flex flex-col gap-2">
            <label for="newRate" class="text-sm">{{ t("vatRate") }}</label>
            <InputNumber
              v-model="newRate"
              v-decimal-point
              input-id="newRate"
              locale="de-DE"
              :max-fraction-digits="2"
              suffix=" %"
              :input-style="{ width: '8rem' }"
            />
          </div>
          <Button type="submit" :label="t('addVatRate')" icon="pi pi-plus" outlined />
        </form>
      </template>
    </Card>
  </div>
</template>
