<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useTheme } from 'vuetify'
import { mdiShieldCheckOutline } from '@mdi/js'
import { api } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { VChart } from '@/plugins/echarts'
import { chartColors, netWorthOption } from '@/utils/charts'
import { formatAmount, formatSignedAmount } from '@/utils/format'
import {
  contributionText,
  monthsCoveredColor,
  monthsCoveredText,
  NET_WORTH_LENGTHS,
  netWorthChange,
  type EmergencyFund,
  type NetWorthLength,
  type NetWorthPoint,
  type SavingsGoal,
} from '@/utils/savings'

/** Tableau de bord : évolution du patrimoine (G5), épargne de précaution (J3) et objectifs d'épargne (J1, J2). */
const theme = useTheme()
const length = ref<NetWorthLength>(12)
const points = ref<NetWorthPoint[]>([])
const fund = ref<EmergencyFund | null>(null)
const goals = ref<SavingsGoal[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

/** Objectifs affichés sur le tableau de bord : les plus proches de leur date cible. */
const MAX_GOALS = 3

const dark = computed(() => theme.global.current.value.dark)
const chart = computed(() => netWorthOption(points.value, chartColors(dark.value)))
const current = computed(() => points.value.at(-1)?.total ?? 0)
const change = computed(() => netWorthChange(points.value))
const shownGoals = computed(() => goals.value.slice(0, MAX_GOALS))

async function loadNetWorth() {
  points.value = (
    await unwrap(api.GET('/api/net-worth', { params: { query: { months: length.value } } }))
  ).points
}

async function load() {
  loading.value = true
  try {
    const [, emergency, list] = await Promise.all([
      loadNetWorth(),
      unwrap(api.GET('/api/emergency-fund')),
      unwrap(api.GET('/api/savings-goals')),
    ])
    fund.value = emergency
    goals.value = list
    error.value = null
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

watch(length, () =>
  loadNetWorth().catch((e) => (error.value = e instanceof ApiError ? e.message : 'Chargement impossible.')),
)
onMounted(load)
</script>

<template>
  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <!-- G5 : évolution du patrimoine net. -->
  <v-card class="mb-4" data-testid="net-worth-chart">
    <v-card-title class="d-flex align-center flex-wrap ga-2">
      <span class="mr-auto">Patrimoine net</span>
      <v-btn-toggle
        v-model="length"
        mandatory
        density="compact"
        variant="outlined"
        color="primary"
        divided
        data-testid="net-worth-length"
      >
        <v-btn v-for="l in NET_WORTH_LENGTHS" :key="l" :value="l" size="small">
          {{ l === 60 ? '5 ans' : `${l} mois` }}
        </v-btn>
      </v-btn-toggle>
    </v-card-title>
    <v-card-subtitle class="text-wrap">
      <strong>{{ formatAmount(current) }}</strong>
      <span :class="change < 0 ? 'text-error' : 'text-success'">
        · {{ formatSignedAmount(change) }} sur {{ length === 60 ? '5 ans' : `${length} mois` }}
      </span>
    </v-card-subtitle>
    <v-progress-linear v-if="loading" indeterminate color="primary" />
    <div class="px-2" style="height: 280px">
      <VChart
        :option="chart"
        :theme="dark ? 'dark' : undefined"
        autoresize
        aria-label="Patrimoine net en fin de chaque période, au total et par type de compte"
      />
    </div>
  </v-card>

  <v-row class="mb-4">
    <!-- J3 : épargne de précaution. -->
    <v-col v-if="fund" cols="12" md="5">
      <v-card class="h-100" :to="{ name: 'savings' }" data-testid="emergency-fund-summary">
        <v-card-item>
          <template #prepend>
            <v-avatar :color="monthsCoveredColor(fund.monthsCovered)" variant="tonal">
              <v-icon :icon="mdiShieldCheckOutline" />
            </v-avatar>
          </template>
          <v-card-title>Épargne de précaution</v-card-title>
          <v-card-subtitle class="text-wrap">{{ monthsCoveredText(fund) }}</v-card-subtitle>
        </v-card-item>
        <v-card-text class="text-h6 font-weight-bold pt-0">{{ formatAmount(fund.amount) }}</v-card-text>
      </v-card>
    </v-col>

    <!-- J1, J2 : objectifs d'épargne. -->
    <v-col cols="12" :md="fund ? 7 : 12">
      <v-card class="h-100" data-testid="goals-summary">
        <v-card-title>Objectifs d'épargne</v-card-title>
        <v-list v-if="shownGoals.length" lines="two" density="compact">
          <v-list-item v-for="goal in shownGoals" :key="goal.id" :to="{ name: 'savings' }">
            <v-list-item-title class="d-flex">
              <span class="mr-auto text-truncate">{{ goal.name }}</span>
              <span class="text-body-2 text-no-wrap">{{ goal.percent }} %</span>
            </v-list-item-title>
            <v-progress-linear :model-value="goal.percent" color="primary" height="6" rounded class="my-1" />
            <v-list-item-subtitle class="text-wrap">{{ contributionText(goal) }}</v-list-item-subtitle>
          </v-list-item>
        </v-list>
        <v-card-text v-else class="text-medium-emphasis">Aucun objectif d'épargne.</v-card-text>
        <v-card-actions>
          <v-btn :to="{ name: 'savings' }" variant="text" color="primary">
            {{ goals.length > MAX_GOALS ? `Voir les ${goals.length} objectifs` : 'Gérer les objectifs' }}
          </v-btn>
        </v-card-actions>
      </v-card>
    </v-col>
  </v-row>
</template>
