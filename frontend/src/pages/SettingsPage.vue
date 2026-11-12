<script setup lang="ts">
import { useQueryClient } from "@tanstack/vue-query";
import Button from "primevue/button";
import Card from "primevue/card";
import Column from "primevue/column";
import DataTable from "primevue/datatable";
import Dialog from "primevue/dialog";
import InputNumber from "primevue/inputnumber";
import InputText from "primevue/inputtext";
import Textarea from "primevue/textarea";
import { useToast } from "primevue/usetoast";
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import { onBeforeRouteLeave } from "vue-router";
import { ApiError } from "@/api/http";
import { odata } from "@/api/odata";
import { useCemeteriesQuery, useSettingsQuery, useVatRatesQuery } from "@/api/queries";
import PageHeader from "@/components/PageHeader.vue";
import { useConfirmAsync } from "@/composables/useConfirmAsync";
import { useErrorToast } from "@/composables/useErrorToast";
import { useShortTablePaging } from "@/composables/usePageReport";
import { vDecimalPoint } from "@/directives/decimalPoint";
import { formatVatRate } from "@/lib/format";

const { t } = useI18n();
const paging = useShortTablePaging();
const toast = useToast();
const { showError } = useErrorToast();
const ask = useConfirmAsync();
const queryClient = useQueryClient();

const settings = useSettingsQuery();
const form = reactive({ StampWord: "", EndingText: "" });
watch(
  () => settings.data.value,
  (value) => {
    if (value) {
      Object.assign(form, { StampWord: value.StampWord, EndingText: value.EndingText });
    }
  },
  { immediate: true },
);

// Leaving with unsaved edits asks first -- inside the app, and on a reload or a
// closed tab through the browser's own prompt.
const dirty = computed(() => {
  const stored = settings.data.value;
  return !!stored && (form.StampWord !== stored.StampWord || form.EndingText !== stored.EndingText);
});
onBeforeRouteLeave(async () => !dirty.value || (await ask(t("confirmDiscardSettings"))));
function onBeforeUnload(event: BeforeUnloadEvent): void {
  if (dirty.value) {
    event.preventDefault();
  }
}
onMounted(() => window.addEventListener("beforeunload", onBeforeUnload));
onBeforeUnmount(() => window.removeEventListener("beforeunload", onBeforeUnload));

const saving = ref(false);
async function save(): Promise<void> {
  saving.value = true;
  try {
    // no client check: the backend answers StampWordRequired
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

// one change at a time: a double click posted the rate twice, or deleted it
// twice, and the second answer showed as an error
const vatBusy = ref(false);

async function addVatRate(): Promise<void> {
  if (vatBusy.value) {
    return;
  }
  vatBusy.value = true;
  try {
    if (newRate.value == null || !(newRate.value > 0 && newRate.value < 100)) {
      throw new ApiError("InvalidVatRate");
    }
    await odata.VatRates().create({ Rate: newRate.value }).execute();
    newRate.value = null;
    await queryClient.invalidateQueries();
  } catch (error) {
    showError(error);
  } finally {
    vatBusy.value = false;
  }
}

async function deleteVatRate(id: number, rate: number): Promise<void> {
  if (vatBusy.value || !(await ask(t("confirmDeleteVatRate", [formatVatRate(rate)])))) {
    return;
  }
  vatBusy.value = true;
  try {
    await odata.VatRates(id).delete().execute();
    await queryClient.invalidateQueries();
  } catch (error) {
    showError(error);
  } finally {
    vatBusy.value = false;
  }
}

const cemeteries = useCemeteriesQuery();
const newCemetery = ref("");
// one change at a time, like the VAT rates
const cemeteryBusy = ref(false);

async function addCemetery(): Promise<void> {
  if (cemeteryBusy.value) {
    return;
  }
  cemeteryBusy.value = true;
  try {
    // no client check: the backend answers CemeteryNameRequired and, ignoring
    // case, CemeteryNameExists
    await odata.Cemeteries().create({ Name: newCemetery.value }).execute();
    newCemetery.value = "";
    await queryClient.invalidateQueries();
  } catch (error) {
    showError(error);
  } finally {
    cemeteryBusy.value = false;
  }
}

// Graves refer to a cemetery, so a new name is every grave's at once.
const renaming = reactive({ visible: false, id: 0, name: "" });
function startRename(id: number, name: string): void {
  Object.assign(renaming, { visible: true, id, name });
}

async function rename(): Promise<void> {
  if (cemeteryBusy.value) {
    return;
  }
  cemeteryBusy.value = true;
  try {
    await odata.Cemeteries(renaming.id).patch({ Name: renaming.name }).execute();
    renaming.visible = false;
    await queryClient.invalidateQueries();
  } catch (error) {
    showError(error);
  } finally {
    cemeteryBusy.value = false;
  }
}

async function deleteCemetery(id: number, name: string): Promise<void> {
  if (cemeteryBusy.value || !(await ask(t("confirmDeleteCemetery", [name])))) {
    return;
  }
  cemeteryBusy.value = true;
  try {
    // refused with CemeteryInUse while a grave lies there
    await odata.Cemeteries(id).delete().execute();
    await queryClient.invalidateQueries();
  } catch (error) {
    showError(error);
  } finally {
    cemeteryBusy.value = false;
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
          <div>
            <Button type="submit" :label="t('save')" icon="pi pi-check" :loading="saving" />
          </div>
        </form>
      </template>
    </Card>

    <div class="flex min-w-0 flex-col gap-6">
      <Card>
        <template #title>{{ t("settingsVatRates") }}</template>
        <template #content>
          <DataTable
            :value="vatRates.data.value ?? []"
            data-key="Id"
            v-bind="paging"
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
            <Button
              type="submit"
              :label="t('addVatRate')"
              icon="pi pi-plus"
              outlined
              :loading="vatBusy"
            />
          </form>
        </template>
      </Card>

      <Card>
        <template #title>{{ t("settingsCemeteries") }}</template>
        <template #content>
          <DataTable
            :value="cemeteries.data.value ?? []"
            data-key="Id"
            v-bind="paging"
            size="small"
            :loading="cemeteries.isFetching.value"
          >
            <template #empty>{{ t("noData") }}</template>
            <Column :header="t('cemeteryName')">
              <template #body="{ data }">
                <span class="[overflow-wrap:anywhere]">{{ data.Name }}</span>
              </template>
            </Column>
            <Column class="w-0 whitespace-nowrap text-right">
              <template #body="{ data }">
                <Button
                  v-tooltip.left="t('renameCemetery')"
                  icon="pi pi-pencil"
                  text
                  rounded
                  :aria-label="`${t('renameCemetery')}: ${data.Name}`"
                  @click="startRename(data.Id, data.Name)"
                />
                <Button
                  v-tooltip.left="t('delete')"
                  icon="pi pi-trash"
                  severity="danger"
                  text
                  rounded
                  :aria-label="`${t('delete')}: ${data.Name}`"
                  @click="deleteCemetery(data.Id, data.Name)"
                />
              </template>
            </Column>
          </DataTable>
          <form class="mt-4 flex flex-wrap items-end gap-2" @submit.prevent="addCemetery">
            <div class="flex min-w-0 flex-1 flex-col gap-2">
              <label for="newCemetery" class="text-sm">{{ t("cemeteryName") }}</label>
              <InputText id="newCemetery" v-model="newCemetery" class="w-full" />
            </div>
            <Button
              type="submit"
              :label="t('addCemetery')"
              icon="pi pi-plus"
              outlined
              :loading="cemeteryBusy"
            />
          </form>
        </template>
      </Card>
    </div>
  </div>

  <Dialog
    v-model:visible="renaming.visible"
    modal
    :header="t('renameCemetery')"
    :style="{ width: '26rem' }"
    :breakpoints="{ '640px': '100vw' }"
  >
    <form id="renameCemeteryForm" class="flex flex-col gap-2" @submit.prevent="rename">
      <label for="renameCemetery" class="font-medium">{{ t("cemeteryName") }}</label>
      <InputText id="renameCemetery" v-model="renaming.name" autofocus />
      <small class="text-muted-color">{{ t("renameCemeteryHint") }}</small>
    </form>
    <template #footer>
      <Button :label="t('cancel')" severity="secondary" text @click="renaming.visible = false" />
      <Button
        type="submit"
        form="renameCemeteryForm"
        :label="t('save')"
        icon="pi pi-check"
        :loading="cemeteryBusy"
      />
    </template>
  </Dialog>
</template>
