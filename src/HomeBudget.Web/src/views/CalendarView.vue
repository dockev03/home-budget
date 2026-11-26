<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import { mdiAlertOutline, mdiBankTransfer, mdiRepeat } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import PeriodNavigator from '@/components/PeriodNavigator.vue'
import { useReferenceStore } from '@/stores/reference'
import {
  calendarStatuses,
  calendarWeeks,
  dayTotal,
  isLate,
  WEEK_DAYS,
  type CalendarDay,
} from '@/utils/calendar'
import { formatAmount, formatDayHeading, formatSignedAmount, todayIso } from '@/utils/format'

/** Calendrier des échéances de la période (E8) : à venir, à valider, passées ou sautées. */
const route = useRoute()
const router = useRouter()
const reference = useReferenceStore()
const { smAndUp } = useDisplay()

const calendar = ref<Schemas['CalendarDto'] | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const selected = ref<string | null>(null)
const today = todayIso()

const periodKey = computed(() => (route.params.period as string | undefined) || null)
const weeks = computed(() =>
  calendar.value
    ? calendarWeeks(calendar.value.period.start, calendar.value.period.end, calendar.value.entries)
    : [],
)
/** Jours affichés en liste : le jour choisi, ou tous les jours qui ont des échéances. */
const listedDays = computed(() =>
  weeks.value
    .flat()
    .filter((d) => !d.outside && d.entries.length > 0 && (!selected.value || d.date === selected.value)),
)

async function load() {
  loading.value = true
  error.value = null
  selected.value = null
  try {
    let key = periodKey.value
    if (!key) key = (await unwrap(api.GET('/api/periods/current'))).key
    const [data] = await Promise.all([
      unwrap(api.GET('/api/calendar/{periodKey}', { params: { path: { periodKey: key } } })),
      reference.load(),
    ])
    calendar.value = data
  } catch (e) {
    calendar.value = null
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

function goTo(key: string | null) {
  router.push(key ? { name: 'calendar', params: { period: key } } : { name: 'calendar' })
}

function select(day: CalendarDay) {
  if (day.outside || day.entries.length === 0) return
  selected.value = selected.value === day.date ? null : day.date
}

watch(periodKey, load, { immediate: true })
</script>

<template>
  <div class="d-flex align-center flex-wrap mb-4 ga-2">
    <h1 class="text-h5 font-weight-bold mr-auto">Calendrier des échéances</h1>
    <PeriodNavigator :period="calendar?.period ?? null" @go="goTo" />
  </div>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
  <v-progress-linear v-if="loading" indeterminate color="primary" class="mb-2" />

  <template v-if="calendar">
    <v-row dense class="mb-2">
      <v-col cols="6">
        <v-card variant="tonal" color="success">
          <v-card-text>
            <div class="text-caption">Entrées prévues</div>
            <div class="text-h6 font-weight-bold" data-testid="calendar-income">
              {{ formatAmount(calendar.income) }}
            </div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="6">
        <v-card variant="tonal" color="error">
          <v-card-text>
            <div class="text-caption">Sorties prévues</div>
            <div class="text-h6 font-weight-bold" data-testid="calendar-expenses">
              {{ formatAmount(calendar.expenses) }}
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-card class="mb-4" data-testid="calendar-grid">
      <div class="hb-calendar">
        <div
          v-for="name in WEEK_DAYS"
          :key="name"
          class="hb-calendar__head text-caption text-medium-emphasis"
        >
          {{ name }}
        </div>
        <template v-for="(week, w) in weeks" :key="w">
          <button
            v-for="day in week"
            :key="day.date"
            type="button"
            class="hb-calendar__day"
            :class="{
              'hb-calendar__day--outside': day.outside,
              'hb-calendar__day--today': day.date === today,
              'hb-calendar__day--selected': day.date === selected,
            }"
            :disabled="day.outside || day.entries.length === 0"
            :aria-label="
              day.entries.length
                ? `${formatDayHeading(day.date)} : ${day.entries.length} échéance(s)`
                : undefined
            "
            @click="select(day)"
          >
            <span class="text-caption">{{ day.day }}</span>
            <template v-if="!day.outside && day.entries.length">
              <span class="hb-calendar__dots">
                <span
                  v-for="(e, i) in day.entries.slice(0, 4)"
                  :key="i"
                  class="hb-calendar__dot"
                  :class="`bg-${calendarStatuses[e.status].color}`"
                />
              </span>
              <span
                v-if="smAndUp"
                class="text-caption text-truncate"
                :class="dayTotal(day.entries) < 0 ? 'text-error' : 'text-success'"
              >
                {{ dayTotal(day.entries) !== 0 ? formatSignedAmount(dayTotal(day.entries)) : '' }}
              </span>
            </template>
          </button>
        </template>
      </div>
    </v-card>

    <div class="d-flex flex-wrap ga-2 mb-4">
      <v-chip v-for="(s, key) in calendarStatuses" :key="key" size="small" :color="s.color" variant="tonal">{{
        s.title
      }}</v-chip>
      <v-chip v-if="selected" size="small" closable @click:close="selected = null">Un seul jour</v-chip>
    </div>

    <v-alert
      v-if="calendar.entries.length === 0"
      type="info"
      variant="tonal"
      text="Aucune échéance sur cette période."
    />

    <v-card v-for="day in listedDays" :key="day.date" class="mb-3" :data-testid="`calendar-day-${day.date}`">
      <v-card-subtitle class="pt-3">{{ formatDayHeading(day.date) }}</v-card-subtitle>
      <v-list density="compact" lines="two">
        <v-list-item
          v-for="e in day.entries"
          :key="`${e.recurrenceId}-${e.scheduledDate}`"
          :class="{ 'text-disabled': e.status === 'Skipped' }"
        >
          <template #prepend>
            <v-icon
              :icon="e.toAccountId ? mdiBankTransfer : mdiRepeat"
              :color="calendarStatuses[e.status].color"
            />
          </template>
          <v-list-item-title>{{ e.label }}</v-list-item-title>
          <v-list-item-subtitle>
            {{ reference.accountById.get(e.accountId)?.name ?? 'Compte' }}
            <template v-if="e.toAccountId">
              → {{ reference.accountById.get(e.toAccountId)?.name ?? 'Compte' }}</template
            >
            <template v-else> · {{ reference.categoryLabel(e.categoryId) }}</template>
            <template v-if="e.date !== e.scheduledDate"> · reportée</template>
          </v-list-item-subtitle>
          <template #append>
            <div class="text-right">
              <div class="font-weight-medium" :class="e.amount < 0 && !e.toAccountId ? 'text-error' : ''">
                {{ e.toAccountId ? formatAmount(e.amount) : formatSignedAmount(e.amount) }}
              </div>
              <v-chip size="x-small" :color="calendarStatuses[e.status].color" variant="tonal">
                <v-icon v-if="isLate(e, today)" :icon="mdiAlertOutline" start />
                {{ isLate(e, today) ? 'En retard' : calendarStatuses[e.status].title }}
              </v-chip>
            </div>
          </template>
        </v-list-item>
      </v-list>
      <v-card-actions v-if="day.entries.some((e) => e.status === 'Pending')">
        <v-btn :to="{ name: 'pending' }" variant="text" color="primary" size="small"
          >Valider les échéances</v-btn
        >
      </v-card-actions>
    </v-card>
  </template>
</template>

<style scoped>
.hb-calendar {
  display: grid;
  grid-template-columns: repeat(7, minmax(0, 1fr));
}

.hb-calendar__head {
  padding: 6px 4px;
  text-align: center;
}

.hb-calendar__day {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  min-height: 64px;
  padding: 4px 2px;
  border-top: 1px solid rgba(var(--v-border-color), var(--v-border-opacity));
  background: none;
  color: inherit;
  cursor: pointer;
}

.hb-calendar__day:disabled {
  cursor: default;
}

.hb-calendar__day--outside {
  opacity: 0.35;
}

.hb-calendar__day--today > span:first-child {
  font-weight: 700;
  color: rgb(var(--v-theme-primary));
}

.hb-calendar__day--selected {
  background: rgba(var(--v-theme-primary), 0.12);
}

.hb-calendar__dots {
  display: flex;
  gap: 3px;
}

.hb-calendar__dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
}

.hb-calendar__day .text-truncate {
  max-width: 100%;
}
</style>
