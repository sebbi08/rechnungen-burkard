<script setup lang="ts">
import Button from "primevue/button";
import Popover from "primevue/popover";
import { ref } from "vue";
import { useI18n } from "vue-i18n";
import type { GraveStatusRecipient } from "@/api/endpoints";

/** A grave's recipients in the grave overview: nothing for a grave without
 *  any, the name of its only one, and for several a button "{n} Empfänger"
 *  that opens their names, one per line. */
defineProps<{ recipients: GraveStatusRecipient[] }>();

const { t } = useI18n();
const popover = ref<InstanceType<typeof Popover>>();
const open = ref(false);
</script>

<template>
  <span v-if="recipients.length === 1" class="[overflow-wrap:anywhere]">
    {{ recipients[0].Name }}
  </span>
  <template v-else-if="recipients.length > 1">
    <Button
      :label="t('graveRecipientsMany', [recipients.length])"
      link
      class="p-0 whitespace-nowrap"
      aria-haspopup="true"
      :aria-expanded="open"
      @click="popover?.toggle($event)"
    />
    <Popover ref="popover" @show="open = true" @hide="open = false">
      <!-- a long name wraps rather than carrying the list past a phone's edge -->
      <ul class="m-0 max-w-[min(24rem,calc(100vw-3rem))] list-none p-0 [overflow-wrap:anywhere]">
        <li v-for="recipient in recipients" :key="recipient.Id">{{ recipient.Name }}</li>
      </ul>
    </Popover>
  </template>
</template>
