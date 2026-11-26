<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { mdiAlertOutline, mdiCalendarToday, mdiChevronLeft, mdiChevronRight } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { changeColor, changeLabel } from '@/utils/annualReport'
import { formatAmount, formatSignedAmount } from '@/utils/format'
import { iconPath } from '@/utils/icons'
import { categoryKinds } from '@/utils/labels'

/** Récapitulatif annuel par catégorie, comparé à l'année précédente (P3). */
const route = useRoute()
const router = useRouter()

const report = ref<Schemas['AnnualReportDto'] | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

const currentYear = new Date().getFullYear()
const year = computed(() => Number(route.params.year) || currentYear)

const sections = computed(() =>
  categoryKinds.map((kind) => ({
    ...kind,
    lines: report.value?.categories.filter((c) => c.kind === kind.value) ?? [],
  })),
)

const summaryRows = computed(() => {
  const r = report.value
  if (!r) return []
  return [
    { title: 'Revenus', kind: 'Income', actual: r.actual.income, previous: r.previous.income },
    { title: 'Dépenses fixes', kind: 'Fixed', actual: -r.actual.fixed, previous: -r.previous.fixed },
    {
      title: 'Dépenses variables',
      kind: 'Variable',
      actual: -r.actual.variable,
      previous: -r.previous.variable,
    },
    { title: 'Épargne', kind: 'Savings', actual: -r.actual.savings, previous: -r.previous.savings },
  ] as const
})

async function load() {
  loading.value = true
  error.value = null
  try {
    report.value = await unwrap(
      api.GET('/api/reports/annual/{year}', { params: { path: { year: year.value } } }),
    )
  } catch (e) {
    report.value = null
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

function goTo(target: number | null) {
  router.push(
    target && target !== currentYear ? { name: 'annual', params: { year: target } } : { name: 'annual' },
  )
}

watch(year, load, { immediate: true })
</script>

<template>
  <div class="d-flex align-center flex-wrap ga-2 mb-4">
    <h1 class="text-h5 font-weight-bold mr-auto">Bilan annuel</h1>
    <div class="d-flex align-center">
      <v-btn :icon="mdiChevronLeft" variant="text" aria-label="Année précédente" @click="goTo(year - 1)" />
      <div class="text-subtitle-1 font-weight-bold text-center" style="min-width: 80px">{{ year }}</div>
      <v-btn :icon="mdiChevronRight" variant="text" aria-label="Année suivante" @click="goTo(year + 1)" />
      <v-btn
        v-if="year !== currentYear"
        :icon="mdiCalendarToday"
        variant="text"
        aria-label="Année en cours"
        @click="goTo(null)"
      />
    </div>
  </div>

  <v-progress-linear v-if="loading" indeterminate color="primary" class="mb-2" />
  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <template v-if="report">
    <v-card class="mb-4" title="Récapitulatif">
      <v-table density="comfortable">
        <thead>
          <tr>
            <th></th>
            <th class="text-right">{{ report.year }}</th>
            <th class="text-right">{{ report.year - 1 }}</th>
            <th class="text-right">Évolution</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in summaryRows" :key="row.title">
            <td>{{ row.title }}</td>
            <td class="text-right text-no-wrap">{{ formatSignedAmount(row.actual) }}</td>
            <td class="text-right text-no-wrap text-medium-emphasis">
              {{ formatSignedAmount(row.previous) }}
            </td>
            <td
              class="text-right text-no-wrap"
              :class="`text-${changeColor(row.kind, Math.abs(row.actual) - Math.abs(row.previous))}`"
            >
              {{ formatSignedAmount(Math.abs(row.actual) - Math.abs(row.previous)) }}
            </td>
          </tr>
          <tr class="font-weight-bold">
            <td>Restant</td>
            <td class="text-right text-no-wrap">{{ formatAmount(report.actual.remaining) }}</td>
            <td class="text-right text-no-wrap">{{ formatAmount(report.previous.remaining) }}</td>
            <td class="text-right text-no-wrap">
              {{ formatSignedAmount(report.actual.remaining - report.previous.remaining) }}
            </td>
          </tr>
        </tbody>
      </v-table>
      <v-alert
        v-if="report.uncategorized !== 0"
        type="warning"
        variant="tonal"
        density="compact"
        class="ma-4"
        :icon="mdiAlertOutline"
      >
        {{ formatSignedAmount(report.uncategorized) }} d'opérations sans catégorie, hors récapitulatif.
      </v-alert>
    </v-card>

    <template v-for="section in sections" :key="section.value">
      <v-card v-if="section.lines.length" class="mb-4" :title="section.title">
        <v-list lines="two">
          <v-list-item
            v-for="line in section.lines"
            :key="line.categoryId"
            :class="{ 'pl-12': line.parentId }"
            :data-testid="`annual-${line.name}`"
          >
            <template #prepend>
              <v-avatar :color="line.color ?? 'primary'" variant="tonal" :size="line.parentId ? 28 : 36">
                <v-icon :icon="iconPath(line.icon)" :size="line.parentId ? 16 : 20" />
              </v-avatar>
            </template>
            <v-list-item-title class="d-flex">
              <span class="text-truncate mr-2">{{ line.name }}</span>
              <v-spacer />
              <strong class="text-no-wrap">{{ formatAmount(line.actual) }}</strong>
            </v-list-item-title>
            <v-list-item-subtitle class="d-flex">
              <span class="text-truncate mr-2"
                >{{ report.year - 1 }} : {{ formatAmount(line.previousActual) }}</span
              >
              <v-spacer />
              <span class="text-no-wrap" :class="`text-${changeColor(line.kind, line.difference)}`">
                {{ changeLabel(line.difference, line.changePercent) }}
              </span>
            </v-list-item-subtitle>
          </v-list-item>
        </v-list>
      </v-card>
    </template>
    <v-card v-if="report.categories.length === 0" class="pa-6 text-center text-medium-emphasis">
      Aucune opération sur {{ report.year }} ni {{ report.year - 1 }}.
    </v-card>
    <p class="text-caption text-medium-emphasis">
      L'année réunit les 12 périodes budgétaires qui portent son nom ; les virements sont exclus, sauf vers
      l'épargne.
    </p>
  </template>
</template>
