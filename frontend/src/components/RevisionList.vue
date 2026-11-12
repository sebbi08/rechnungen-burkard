<script setup lang="ts">
import Accordion from "primevue/accordion";
import AccordionContent from "primevue/accordioncontent";
import AccordionHeader from "primevue/accordionheader";
import AccordionPanel from "primevue/accordionpanel";
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { formatDate } from "@/lib/format";
import type { RevisionView } from "@/lib/revisions";
import RevisionPanel from "./RevisionPanel.vue";

/** Every version of an invoice or a quote, newest first: the caller builds
 *  them (buildRevisions, buildQuoteRevisions). */
const props = defineProps<{ revisions: RevisionView[]; kind: "invoice" | "quote" }>();
const { t } = useI18n();

// the current revision opens; the history stays collapsed
const opened = computed(() => (props.revisions.length ? [String(props.revisions[0].Id)] : []));
</script>

<template>
  <Accordion :value="opened" multiple>
    <AccordionPanel v-for="revision in revisions" :key="revision.Id" :value="String(revision.Id)">
      <AccordionHeader>
        <span class="flex flex-wrap items-baseline gap-x-3">
          <span class="font-semibold">
            {{
              revision.RevisionNumber === 0
                ? t("revisionOriginal")
                : t("revisionLabel", [revision.RevisionNumber])
            }}
          </span>
          <span class="text-sm text-muted-color tabular-nums">
            {{ revision.Number }} · {{ formatDate(revision.GeneratedAt) }}
          </span>
        </span>
      </AccordionHeader>
      <!-- the content is a grid whose wrapper would grow to the table's width,
           which pushed the columns past the card instead of letting it scroll -->
      <AccordionContent :pt="{ contentWrapper: { class: 'min-w-0' } }">
        <RevisionPanel :revision="revision" :kind="kind" />
      </AccordionContent>
    </AccordionPanel>
  </Accordion>
</template>
