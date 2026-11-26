<script setup lang="ts">
import Button from "primevue/button";
import Card from "primevue/card";
import Message from "primevue/message";
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { RouterLink } from "vue-router";
import { useAuditEntriesQuery } from "@/api/queries";
import {
  type AuditChange,
  auditActionText,
  auditChangeHead,
  formatAuditValue,
  parseChanges,
  parseDetails,
  parseInvoiceValue,
} from "@/lib/audit";
import { formatDateTime } from "@/lib/format";

/** The Verlauf of a grave or of an invoice chain, newest first. */
const props = defineProps<{ graveId?: number; invoiceRootId?: number }>();
const { t } = useI18n();

const subject = computed(() =>
  props.graveId != null
    ? { graveId: props.graveId }
    : props.invoiceRootId != null
      ? { invoiceRootId: props.invoiceRootId }
      : null,
);
const query = useAuditEntriesQuery(subject);

/** The loaded pages in order. A page fetched after an entry was written may
 *  repeat the last one of the page before it, so each entry shows once. */
const rows = computed(() => {
  const seen = new Set<number>();
  return (query.data.value?.pages ?? [])
    .flatMap((page) => page.rows)
    .filter((entry) => !seen.has(entry.Id) && seen.add(entry.Id));
});

interface ValuePart {
  text: string;
  /** Where the value links to, for an invoice. */
  to?: string;
}

interface ChangeLine {
  head: string;
  /** "+" for an added value, "−" for a removed one. */
  sign: string;
  /** The old value, an arrow and the new one, or the one value there is. */
  parts: ValuePart[];
}

/** An invoice value links to the invoice's page: the grave's, where the entry
 *  belongs to one. */
function valuePart(change: AuditChange, value: string | null, graveId: number | null): ValuePart {
  const text = formatAuditValue(change, value, t);
  const invoice = change.type === "invoice" && value != null ? parseInvoiceValue(value) : null;
  if (!invoice) {
    return { text };
  }
  return {
    text,
    to:
      graveId != null
        ? `/graves/${graveId}/invoices/${invoice.rootId}`
        : `/invoices/${invoice.rootId}`,
  };
}

function changeLine(change: AuditChange, graveId: number | null): ChangeLine {
  const head = auditChangeHead(change, t);
  // a missing end is a value ("ohne Ende"), not an absent one
  const open = change.field === "end";
  if (change.old == null && change.new == null) {
    return { head, sign: "", parts: [] };
  }
  if (change.old == null && !open) {
    return { head, sign: "+", parts: [valuePart(change, change.new, graveId)] };
  }
  if (change.new == null && !open) {
    return { head, sign: "−", parts: [valuePart(change, change.old, graveId)] };
  }
  return {
    head,
    sign: "",
    parts: [
      valuePart(change, change.old, graveId),
      { text: "→" },
      valuePart(change, change.new, graveId),
    ],
  };
}

const entries = computed(() =>
  rows.value.map((entry) => ({
    id: entry.Id,
    // the API serialises the UTC instant with its offset, but be safe without one
    when: formatDateTime(
      /(Z|[+-]\d\d:?\d\d)$/i.test(entry.OccurredAt) ? entry.OccurredAt : `${entry.OccurredAt}Z`,
    ),
    action: auditActionText(entry.Action, parseDetails(entry.DetailsJson), t),
    lines: parseChanges(entry.ChangesJson).map((change) =>
      changeLine(change, entry.GraveId ?? null),
    ),
  })),
);
const hasMore = computed(() => query.hasNextPage.value);
</script>

<template>
  <Card data-testid="audit-trail">
    <template #title>{{ t("auditTitle") }}</template>
    <template #content>
      <Message v-if="query.isError.value && !entries.length" severity="error">
        {{ t("loadFailed") }}
      </Message>
      <p v-else-if="query.isSuccess.value && !entries.length" class="m-0 text-muted-color">
        {{ t("auditEmpty") }}
      </p>
      <ol v-else class="m-0 flex list-none flex-col gap-4 p-0">
        <li v-for="entry in entries" :key="entry.id" data-testid="audit-entry">
          <div class="flex flex-wrap items-baseline gap-x-3">
            <span class="text-sm tabular-nums text-muted-color">{{ entry.when }}</span>
            <span class="font-semibold">{{ entry.action }}</span>
          </div>
          <ul
            v-if="entry.lines.length"
            class="m-0 mt-1 flex list-none flex-col gap-0.5 p-0 pl-4 text-sm"
          >
            <li v-for="(line, index) in entry.lines" :key="index">
              <!-- "Feld: alt → neu"; the spaces are interpolated, since the
                   template drops whitespace that opens an element -->
              <span class="text-muted-color"
                >{{ line.head }}<template v-if="line.parts.length">:</template></span
              >
              <template v-if="line.parts.length">
                <template v-if="line.sign">{{ " " }}{{ line.sign }}</template>
                <template v-for="(part, at) in line.parts" :key="at">
                  {{ " " }}
                  <RouterLink v-if="part.to" :to="part.to" class="text-primary hover:underline">
                    {{ part.text }}
                  </RouterLink>
                  <template v-else>{{ part.text }}</template>
                </template>
              </template>
            </li>
          </ul>
        </li>
      </ol>
      <!-- a further page that failed keeps the entries already shown -->
      <Message v-if="query.isError.value && entries.length" class="mt-4" severity="error">
        {{ t("loadFailed") }}
      </Message>
      <Button
        v-if="hasMore"
        class="mt-4"
        :label="t('auditMore')"
        severity="secondary"
        outlined
        :loading="query.isFetchingNextPage.value"
        @click="query.fetchNextPage()"
      />
    </template>
  </Card>
</template>
