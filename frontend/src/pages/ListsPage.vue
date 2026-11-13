<script setup lang="ts">
import Button from "primevue/button";
import Card from "primevue/card";
import DatePicker from "primevue/datepicker";
import SelectButton from "primevue/selectbutton";
import { computed, ref } from "vue";
import { useI18n } from "vue-i18n";
import { type CareListServices, downloadCareList, downloadPlantingList } from "@/api/endpoints";
import PageHeader from "@/components/PageHeader.vue";
import { useErrorToast } from "@/composables/useErrorToast";
import { downloadBlob } from "@/lib/download";
import { QUARTERS, type QuarterName, quarterLabelKey, quarterOf } from "@/lib/quarter";

const { t } = useI18n();
const { showError } = useErrorToast();

// the backend takes the years 2000 to 2100
const minYear = new Date(2000, 0, 1);
const maxYear = new Date(2100, 11, 31);

const today = new Date();
const careYear = ref<Date | null>(new Date(today.getFullYear(), 0, 1));
const plantingYear = ref<Date | null>(new Date(today.getFullYear(), 0, 1));
const services = ref<CareListServices>("none");
const quarter = ref<QuarterName>(quarterOf(today));
const careBusy = ref(false);
const plantingBusy = ref(false);

const serviceOptions = computed(() =>
  (
    [
      ["none", "careListServicesNone"],
      ["care", "careListServicesCare"],
      ["all", "careListServicesAll"],
    ] as const
  ).map(([value, key]) => ({ value, label: t(key) })),
);
const quarterOptions = computed(() =>
  QUARTERS.map((value) => ({ value, label: t(quarterLabelKey(value)) })),
);

/** The year a picker holds; a field left empty counts as the current year. */
function yearOf(field: Date | null): number {
  return (field ?? today).getFullYear();
}

/** A field left empty shows the current year again. */
function restoreYears(): void {
  careYear.value ??= new Date(today.getFullYear(), 0, 1);
  plantingYear.value ??= new Date(today.getFullYear(), 0, 1);
}

async function downloadCare(): Promise<void> {
  careBusy.value = true;
  try {
    const { blob, fileName } = await downloadCareList(yearOf(careYear.value), services.value);
    downloadBlob(blob, fileName);
  } catch (error) {
    showError(error);
  } finally {
    careBusy.value = false;
  }
}

async function downloadPlanting(): Promise<void> {
  plantingBusy.value = true;
  try {
    const { blob, fileName } = await downloadPlantingList(
      yearOf(plantingYear.value),
      quarter.value,
    );
    downloadBlob(blob, fileName);
  } catch (error) {
    showError(error);
  } finally {
    plantingBusy.value = false;
  }
}
</script>

<template>
  <PageHeader :title="t('menuLists')" />

  <div class="flex flex-col gap-6">
    <Card>
      <template #title>{{ t("careList") }}</template>
      <template #content>
        <p class="mt-0 text-muted-color">{{ t("careListHint") }}</p>
        <div class="flex flex-wrap items-center gap-3">
          <DatePicker
            v-model="careYear"
            input-id="careListYear"
            view="year"
            date-format="yy"
            :min-date="minYear"
            :max-date="maxYear"
            :aria-label="t('careListYear')"
            show-icon
            class="w-full sm:w-36"
            @hide="restoreYears"
          />
          <SelectButton
            v-model="services"
            :options="serviceOptions"
            option-label="label"
            option-value="value"
            :allow-empty="false"
            :aria-label="t('listScope')"
          />
          <Button
            :label="t('downloadPdf')"
            :aria-label="t('careListDownload')"
            icon="pi pi-download"
            outlined
            :loading="careBusy"
            @click="downloadCare"
          />
        </div>
      </template>
    </Card>

    <Card>
      <template #title>{{ t("plantingList") }}</template>
      <template #content>
        <p class="mt-0 text-muted-color">{{ t("plantingListHint") }}</p>
        <div class="flex flex-wrap items-center gap-3">
          <DatePicker
            v-model="plantingYear"
            input-id="plantingListYear"
            view="year"
            date-format="yy"
            :min-date="minYear"
            :max-date="maxYear"
            :aria-label="t('plantingListYear')"
            show-icon
            class="w-full sm:w-36"
            @hide="restoreYears"
          />
          <SelectButton
            v-model="quarter"
            :options="quarterOptions"
            option-label="label"
            option-value="value"
            :allow-empty="false"
            :aria-label="t('listQuarter')"
          />
          <Button
            :label="t('downloadPdf')"
            :aria-label="t('plantingListDownload')"
            icon="pi pi-download"
            outlined
            :loading="plantingBusy"
            @click="downloadPlanting"
          />
        </div>
      </template>
    </Card>
  </div>
</template>
