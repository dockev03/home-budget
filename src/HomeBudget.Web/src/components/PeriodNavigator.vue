<script setup lang="ts">
import { mdiCalendarToday, mdiChevronLeft, mdiChevronRight } from '@mdi/js'
import type { Schemas } from '@/api/client'
import { periodLabel, periodRange } from '@/utils/budget'

/** Navigation entre périodes par flèches (budget, tableau de bord). */
defineProps<{ period: Schemas['PeriodDto'] | null }>()
const emit = defineEmits<{ go: [key: string | null] }>()
</script>

<template>
  <div class="d-flex align-center">
    <v-btn
      :icon="mdiChevronLeft"
      variant="text"
      aria-label="Période précédente"
      :disabled="!period"
      @click="emit('go', period!.previousKey)"
    />
    <div class="text-center" style="min-width: 190px">
      <div class="text-subtitle-1 font-weight-bold">
        {{ period ? periodLabel(period.key) : '…' }}
      </div>
      <div v-if="period" class="text-caption text-medium-emphasis">
        {{ periodRange(period.start, period.end) }}
      </div>
    </div>
    <v-btn
      :icon="mdiChevronRight"
      variant="text"
      aria-label="Période suivante"
      :disabled="!period"
      @click="emit('go', period!.nextKey)"
    />
    <v-btn
      v-if="period && !period.isCurrent"
      :icon="mdiCalendarToday"
      variant="text"
      aria-label="Période en cours"
      @click="emit('go', null)"
    />
  </div>
</template>
