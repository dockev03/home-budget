<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useTheme } from 'vuetify'
import { mdiAlertOutline, mdiCalendarCheckOutline, mdiShieldAlertOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import PeriodNavigator from '@/components/PeriodNavigator.vue'
import { VChart } from '@/plugins/echarts'
import { useAuthStore } from '@/stores/auth'
import { usePendingStore } from '@/stores/pending'
import { useReferenceStore } from '@/stores/reference'
import {
  balanceCurveOption,
  budgetBarsOption,
  chartColors,
  expensesDonutOption,
  historyOption,
  topPayeesOption,
} from '@/utils/charts'
import { categoryRows, formatPercent, kindRows, type ComparedWith } from '@/utils/comparison'
import { formatAmount, formatDate, formatSignedAmount } from '@/utils/format'
import { iconPath } from '@/utils/icons'
import { periodLabel } from '@/utils/budget'
import { activeScenarioIds, HORIZONS, keepKnown, type Scenario } from '@/utils/scenarios'

const auth = useAuthStore()
const reference = useReferenceStore()
const pending = usePendingStore()
const route = useRoute()
const router = useRouter()
const theme = useTheme()

const dashboard = ref<Schemas['DashboardDto'] | null>(null)
const forecast = ref<Schemas['ForecastDto'] | null>(null)
const comparison = ref<Schemas['ComparisonDto'] | null>(null)
const comparedWith = ref<ComparedWith>('previous')
// F4 et F5 : horizon de la prévision et scénarios ajoutés à la courbe.
const horizon = ref(1)
const scenarios = ref<Scenario[]>([])
const selectedScenarios = ref<string[]>([])
const forecastLoading = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)

const periodKey = computed(() => (route.params.period as string | undefined) || null)
const figures = computed(() => dashboard.value?.keyFigures)
const isCurrent = computed(() => dashboard.value?.period.isCurrent ?? false)
const rav = computed(() => forecast.value?.remainingToLive)
const hasForecastAccounts = computed(() => (forecast.value?.accountIds.length ?? 0) > 0)

// Graphiques : thème clair ou sombre suivi (G10).
const dark = computed(() => theme.global.current.value.dark)
const colors = computed(() => chartColors(dark.value))
const chartTheme = computed(() => (dark.value ? 'dark' : undefined))
const donut = computed(() => expensesDonutOption(dashboard.value?.expensesByCategory ?? [], colors.value))
const budgetBars = computed(() => budgetBarsOption(dashboard.value?.budgetVsActual ?? [], colors.value))
const budgetHeight = computed(
  () => `${Math.max(220, (dashboard.value?.budgetVsActual.length ?? 0) * 44 + 60)}px`,
)
const history = computed(() => historyOption(dashboard.value?.history ?? [], colors.value))
const topPayees = computed(() => topPayeesOption(dashboard.value?.topPayees ?? [], colors.value))
const topPayeesHeight = computed(
  () => `${Math.max(160, (dashboard.value?.topPayees.length ?? 0) * 32 + 32)}px`,
)
const curve = computed(() =>
  forecast.value
    ? balanceCurveOption(
        forecast.value.daily,
        forecast.value.threshold,
        colors.value,
        forecast.value.scenario?.daily ?? null,
      )
    : null,
)
const horizonEnd = computed(() => forecast.value?.periods.at(-1))
const forecastTitle = computed(() =>
  horizon.value === 1 ? 'Fin de période' : `Prévision sur ${horizon.value} mois`,
)
const comparisonKinds = computed(() =>
  comparison.value ? kindRows(comparison.value, comparedWith.value) : [],
)
const comparisonCategories = computed(() =>
  comparison.value ? categoryRows(comparison.value, comparedWith.value) : [],
)
const comparisonReference = computed(() =>
  comparison.value ? comparison.value[comparedWith.value].key : null,
)
function deltaClass(favorable: boolean | null): string {
  return favorable === null ? '' : favorable ? 'text-success' : 'text-error'
}

const balanceTitle = computed(() => {
  const date = figures.value?.balanceDate
  return !date || isCurrent.value ? 'Solde actuel' : `Solde au ${formatDate(date)}`
})

async function load() {
  loading.value = true
  error.value = null
  try {
    const key = periodKey.value ?? (await unwrap(api.GET('/api/periods/current'))).key
    const [result, compared] = await Promise.all([
      unwrap(api.GET('/api/dashboard/{periodKey}', { params: { path: { periodKey: key } } })),
      unwrap(api.GET('/api/dashboard/{periodKey}/comparison', { params: { path: { periodKey: key } } })),
    ])
    // La prévision (courbe F2) part toujours de la période en cours.
    if (result.period.isCurrent) await loadForecast()
    else forecast.value = null
    dashboard.value = result
    comparison.value = compared
  } catch (e) {
    dashboard.value = null
    forecast.value = null
    comparison.value = null
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

async function loadForecast() {
  forecastLoading.value = true
  try {
    forecast.value = await unwrap(
      api.GET('/api/forecast', {
        params: { query: { horizon: horizon.value, scenarios: selectedScenarios.value } },
      }),
    )
  } finally {
    forecastLoading.value = false
  }
}

function reloadForecast() {
  loadForecast().catch((e) => (error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'))
}

async function loadScenarios() {
  scenarios.value = await unwrap(api.GET('/api/scenarios'))
  selectedScenarios.value = keepKnown(activeScenarioIds(scenarios.value), scenarios.value)
}

function goTo(key: string | null) {
  void router.push(key ? { name: 'home', params: { period: key } } : { name: 'home' })
}

watch(periodKey, load)
watch([horizon, selectedScenarios], () => {
  if (isCurrent.value) reloadForecast()
})
onMounted(async () => {
  // Les scénarios actifs sont sélectionnés avant le premier calcul de la prévision.
  await loadScenarios().catch(() => undefined)
  await load()
  reference.load(true).catch(() => (error.value = 'Chargement impossible.'))
  pending.load().catch(() => undefined)
})
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Bonjour {{ auth.user?.userName }}</h1>

  <v-alert
    v-if="auth.user && !auth.user.twoFactorEnabled"
    type="warning"
    variant="tonal"
    :icon="mdiShieldAlertOutline"
    class="mb-4"
    title="Double authentification désactivée"
  >
    Protégez l'accès à vos comptes en activant la double authentification.
    <template #append>
      <v-btn :to="{ name: 'security' }" variant="flat" color="warning">Activer</v-btn>
    </template>
  </v-alert>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <v-alert
    v-if="pending.count > 0"
    type="info"
    variant="tonal"
    :icon="mdiCalendarCheckOutline"
    class="mb-4"
    :title="pending.count > 1 ? `${pending.count} échéances à valider` : '1 échéance à valider'"
  >
    <template #append>
      <v-btn :to="{ name: 'pending' }" variant="flat" color="info">Voir</v-btn>
    </template>
  </v-alert>

  <div class="d-flex align-center flex-wrap ga-2 mb-2">
    <h2 class="text-h6 font-weight-bold mr-auto">Tableau de bord</h2>
    <PeriodNavigator :period="dashboard?.period ?? null" @go="goTo" />
  </div>
  <v-progress-linear v-if="loading" indeterminate color="primary" class="mb-2" />

  <template v-if="dashboard && figures">
    <!-- G1 : chiffres clés. -->
    <v-card class="mb-4" data-testid="key-figures">
      <v-row class="pa-4" dense>
        <v-col cols="6" md>
          <div class="text-caption text-medium-emphasis">Revenus</div>
          <div class="text-h6 text-success" data-testid="income">{{ formatAmount(figures.income) }}</div>
        </v-col>
        <v-col cols="6" md>
          <div class="text-caption text-medium-emphasis">Dépenses</div>
          <div class="text-h6" data-testid="expenses">{{ formatAmount(figures.expenses) }}</div>
        </v-col>
        <v-col cols="6" md>
          <div class="text-caption text-medium-emphasis">{{ balanceTitle }}</div>
          <div class="text-h6" :class="(figures.balance ?? 0) < 0 ? 'text-error' : ''">
            {{ figures.balance === null ? '—' : formatAmount(figures.balance) }}
          </div>
        </v-col>
        <v-col cols="6" md>
          <div class="text-caption text-medium-emphasis">Solde prévu</div>
          <div class="text-h6" :class="(figures.forecastBalance ?? 0) < 0 ? 'text-error' : ''">
            {{ figures.forecastBalance === null ? '—' : formatAmount(figures.forecastBalance) }}
          </div>
        </v-col>
        <v-col cols="12" md>
          <div class="text-caption text-medium-emphasis">Reste à vivre</div>
          <div class="text-h6" :class="(figures.remainingToLive ?? 0) < 0 ? 'text-error' : 'text-success'">
            {{ figures.remainingToLive === null ? '—' : formatAmount(figures.remainingToLive) }}
            <span v-if="figures.remainingPerDay !== null" class="text-body-2 text-medium-emphasis">
              ({{ formatAmount(figures.remainingPerDay) }} / jour)
            </span>
          </div>
        </v-col>
      </v-row>
      <v-card-text v-if="!isCurrent" class="pt-0 text-caption text-medium-emphasis">
        Solde prévu et reste à vivre ne sont calculés que pour la période en cours.
      </v-card-text>
    </v-card>

    <!-- F2 : courbe du solde de la période en cours. -->
    <v-card v-if="forecast && isCurrent" class="mb-4" data-testid="forecast">
      <v-card-title>{{ forecastTitle }}</v-card-title>
      <v-card-subtitle class="text-wrap">
        {{ formatDate(forecast.period.start) }} – {{ formatDate(horizonEnd?.end ?? forecast.period.end) }}
        <span v-if="forecast.strategy === 'Average'">
          · par la moyenne des {{ forecast.averageMonths }} derniers mois</span
        >
      </v-card-subtitle>
      <!-- F4 : horizon de la prévision. -->
      <div class="px-4 pt-2">
        <v-btn-toggle
          v-model="horizon"
          mandatory
          density="compact"
          variant="outlined"
          color="primary"
          divided
          data-testid="horizon"
        >
          <v-btn v-for="h in HORIZONS" :key="h.value" :value="h.value" size="small">{{ h.title }}</v-btn>
        </v-btn-toggle>
      </div>
      <v-progress-linear v-if="forecastLoading" indeterminate color="primary" />

      <!-- F5 : scénarios « et si… » ajoutés à la courbe, sans toucher à la prévision. -->
      <div v-if="scenarios.length" class="px-4 pt-2" data-testid="scenario-chips">
        <v-chip-group v-model="selectedScenarios" multiple column selected-class="text-primary">
          <v-chip v-for="sc in scenarios" :key="sc.id" :value="sc.id" filter variant="outlined" size="small">
            {{ sc.name }}
          </v-chip>
        </v-chip-group>
      </div>

      <v-card-text v-if="!hasForecastAccounts" class="text-medium-emphasis">
        Aucun compte n’est inclus dans la prévision : activez l’option sur vos comptes courants.
      </v-card-text>

      <template v-else>
        <v-alert
          v-if="forecast.thresholdDate"
          type="warning"
          variant="tonal"
          density="compact"
          :icon="mdiAlertOutline"
          class="mx-4 mb-2"
        >
          Passage sous le seuil de {{ formatAmount(forecast.threshold) }} prévu le
          <strong>{{ formatDate(forecast.thresholdDate) }}</strong>.
        </v-alert>
        <v-alert
          v-if="
            forecast.scenario?.thresholdDate && forecast.scenario.thresholdDate !== forecast.thresholdDate
          "
          type="warning"
          variant="tonal"
          density="compact"
          :icon="mdiAlertOutline"
          class="mx-4 mb-2"
        >
          Avec les scénarios : passage sous le seuil le
          <strong>{{ formatDate(forecast.scenario.thresholdDate) }}</strong>.
        </v-alert>

        <v-card-text class="py-0">
          Solde prévu le {{ formatDate(forecast.period.end) }} :
          <strong data-testid="end-balance" :class="forecast.endBalance < 0 ? 'text-error' : ''">{{
            formatAmount(forecast.endBalance)
          }}</strong>
          · Reste à vivre : <strong>{{ formatAmount(rav?.perDay ?? 0) }}</strong> par jour pendant
          {{ rav?.daysLeft }} {{ rav?.daysLeft === 1 ? 'jour' : 'jours' }}
        </v-card-text>

        <v-card-text v-if="forecast.scenario" class="py-0" data-testid="scenario-impact">
          Avec les scénarios : <strong>{{ formatAmount(forecast.scenario.endBalance) }}</strong> le
          {{ formatDate(horizonEnd?.end ?? forecast.period.end) }} ({{
            formatSignedAmount(forecast.scenario.impact)
          }})
        </v-card-text>

        <div class="px-2" style="height: 260px">
          <VChart
            v-if="curve"
            :option="curve"
            :theme="chartTheme"
            autoresize
            aria-label="Courbe du solde jour par jour"
          />
        </div>

        <v-expansion-panels variant="accordion" flat class="px-2 pb-2">
          <v-expansion-panel v-if="forecast.periods.length > 1" title="Solde prévu en fin de mois">
            <v-expansion-panel-text>
              <v-table density="compact" data-testid="period-ends">
                <thead>
                  <tr>
                    <th>Période</th>
                    <th class="text-right">Solde prévu</th>
                    <th v-if="forecast.scenario" class="text-right">Avec scénarios</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="p in forecast.periods" :key="p.key">
                    <td>{{ periodLabel(p.key) }}</td>
                    <td class="text-right" :class="p.endBalance < forecast.threshold ? 'text-error' : ''">
                      {{ formatAmount(p.endBalance) }}
                    </td>
                    <td
                      v-if="forecast.scenario"
                      class="text-right"
                      :class="(p.scenarioEndBalance ?? 0) < forecast.threshold ? 'text-error' : ''"
                    >
                      {{ p.scenarioEndBalance === null ? '—' : formatAmount(p.scenarioEndBalance) }}
                    </td>
                  </tr>
                </tbody>
              </v-table>
            </v-expansion-panel-text>
          </v-expansion-panel>
          <v-expansion-panel title="Détail du calcul">
            <v-expansion-panel-text>
              <v-table density="compact">
                <tbody>
                  <tr>
                    <td>Solde actuel des comptes inclus</td>
                    <td class="text-right">{{ formatAmount(forecast.currentBalance) }}</td>
                  </tr>
                  <tr>
                    <td>Opérations déjà saisies à venir</td>
                    <td class="text-right">{{ formatAmount(forecast.futureTransactions) }}</td>
                  </tr>
                  <tr>
                    <td>Échéances de récurrence restantes</td>
                    <td class="text-right">{{ formatAmount(forecast.scheduled) }}</td>
                  </tr>
                  <tr>
                    <td>
                      {{
                        forecast.strategy === 'Average'
                          ? 'Moyenne restante non couverte par une échéance'
                          : 'Budget restant non couvert par une échéance'
                      }}
                      <span v-if="!forecast.includesIncome" class="text-caption">(revenus exclus)</span>
                    </td>
                    <td class="text-right">{{ formatAmount(forecast.budget) }}</td>
                  </tr>
                  <tr class="font-weight-bold">
                    <td>Solde prévu en fin de période</td>
                    <td class="text-right">{{ formatAmount(forecast.endBalance) }}</td>
                  </tr>
                </tbody>
              </v-table>
              <p class="text-caption text-medium-emphasis mt-2">
                Reste à vivre = solde actuel + revenus attendus ({{ formatAmount(rav?.income ?? 0) }}) −
                dépenses fixes restantes ({{ formatAmount(rav?.fixed ?? 0) }}) − épargne prévue ({{
                  formatAmount(rav?.savings ?? 0)
                }}) − seuil de sécurité ({{ formatAmount(rav?.safetyThreshold ?? 0) }}).
              </p>
            </v-expansion-panel-text>
          </v-expansion-panel>
        </v-expansion-panels>
      </template>
    </v-card>

    <v-row class="mb-1">
      <!-- G2 : dépenses par catégorie. -->
      <v-col cols="12" md="6">
        <v-card title="Dépenses par catégorie" class="h-100" data-testid="expenses-chart">
          <div v-if="dashboard.expensesByCategory.length" class="px-2" style="height: 300px">
            <VChart
              :option="donut"
              :theme="chartTheme"
              autoresize
              aria-label="Répartition des dépenses par catégorie"
            />
          </div>
          <v-card-text v-else class="text-medium-emphasis">Aucune dépense sur la période.</v-card-text>
        </v-card>
      </v-col>
      <!-- G3 : budget comparé au réel. -->
      <v-col cols="12" md="6">
        <v-card title="Budget et réel" class="h-100" data-testid="budget-chart">
          <div v-if="dashboard.budgetVsActual.length" class="px-2" :style="{ height: budgetHeight }">
            <VChart
              :option="budgetBars"
              :theme="chartTheme"
              autoresize
              aria-label="Budget comparé au réel par catégorie"
            />
          </div>
          <v-card-text v-else class="text-medium-emphasis"
            >Aucun budget de dépense sur la période.</v-card-text
          >
          <v-card-actions>
            <v-btn
              :to="{ name: 'budget', params: { period: dashboard.period.key } }"
              variant="text"
              color="primary"
              >Voir le budget</v-btn
            >
          </v-card-actions>
        </v-card>
      </v-col>
    </v-row>

    <!-- G6 : comparaison avec la période précédente ou la même période un an avant. -->
    <v-card v-if="comparison" class="mb-4" data-testid="comparison">
      <v-card-title>Comparaison</v-card-title>
      <v-card-subtitle class="text-wrap">
        {{ periodLabel(comparison.current.key) }} comparé à
        {{ comparisonReference ? periodLabel(comparisonReference).toLowerCase() : '' }}
        <span v-if="isCurrent"> · période en cours : réalisé à ce jour</span>
      </v-card-subtitle>
      <div class="px-4 pt-2">
        <v-btn-toggle
          v-model="comparedWith"
          mandatory
          density="compact"
          variant="outlined"
          color="primary"
          divided
        >
          <v-btn value="previous" size="small">Mois précédent</v-btn>
          <v-btn value="lastYear" size="small">Un an avant</v-btn>
        </v-btn-toggle>
      </div>
      <v-table density="compact" class="px-2">
        <thead>
          <tr>
            <th></th>
            <th class="text-right">{{ periodLabel(comparison.current.key) }}</th>
            <th class="text-right">{{ comparisonReference ? periodLabel(comparisonReference) : '' }}</th>
            <th class="text-right">Écart</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in comparisonKinds" :key="r.key" class="font-weight-medium">
            <td>{{ r.label }}</td>
            <td class="text-right text-no-wrap">{{ formatAmount(r.current) }}</td>
            <td class="text-right text-no-wrap">{{ formatAmount(r.reference) }}</td>
            <td class="text-right text-no-wrap" :class="deltaClass(r.favorable)">
              {{ formatSignedAmount(r.delta) }}
              <span class="text-caption">({{ formatPercent(r.percent) }})</span>
            </td>
          </tr>
        </tbody>
      </v-table>
      <v-expansion-panels v-if="comparisonCategories.length" variant="accordion" flat class="px-2 pb-2">
        <v-expansion-panel title="Par catégorie">
          <v-expansion-panel-text>
            <v-table density="compact" data-testid="comparison-categories">
              <tbody>
                <tr v-for="r in comparisonCategories" :key="r.key">
                  <td>{{ r.label }}</td>
                  <td class="text-right text-no-wrap">{{ formatAmount(r.current) }}</td>
                  <td class="text-right text-no-wrap text-medium-emphasis">
                    {{ formatAmount(r.reference) }}
                  </td>
                  <td class="text-right text-no-wrap" :class="deltaClass(r.favorable)">
                    {{ formatSignedAmount(r.delta) }}
                    <span class="text-caption">({{ formatPercent(r.percent) }})</span>
                  </td>
                </tr>
              </tbody>
            </v-table>
          </v-expansion-panel-text>
        </v-expansion-panel>
      </v-expansion-panels>
    </v-card>

    <!-- G4 : 12 dernières périodes. -->
    <v-card title="Revenus, dépenses et épargne sur 12 périodes" class="mb-4" data-testid="history-chart">
      <div class="px-2" style="height: 300px">
        <VChart
          :option="history"
          :theme="chartTheme"
          autoresize
          aria-label="Revenus, dépenses et épargne des 12 dernières périodes"
        />
      </div>
    </v-card>

    <!-- G7 : top des tiers. -->
    <v-card title="Top des tiers" class="mb-4" data-testid="top-payees">
      <div v-if="dashboard.topPayees.length" class="px-2" :style="{ height: topPayeesHeight }">
        <VChart
          :option="topPayees"
          :theme="chartTheme"
          autoresize
          aria-label="Tiers chez qui les dépenses de la période sont les plus élevées"
        />
      </div>
      <v-card-text v-else class="text-medium-emphasis">Aucune dépense rattachée à un tiers.</v-card-text>
    </v-card>
  </template>

  <v-card class="mb-4">
    <v-card-title class="d-flex align-center">
      Comptes
      <v-spacer />
      <span class="text-body-1"
        >Patrimoine net : <strong>{{ formatAmount(reference.netWorth) }}</strong></span
      >
    </v-card-title>
    <v-list v-if="reference.activeAccounts.length">
      <v-list-item
        v-for="account in reference.activeAccounts"
        :key="account.id"
        :title="account.name"
        :to="{ name: 'transactions', query: { accountId: account.id } }"
      >
        <template #prepend>
          <v-avatar :color="account.color ?? 'primary'" variant="tonal" size="36">
            <v-icon :icon="iconPath(account.icon)" size="20" />
          </v-avatar>
        </template>
        <template #append>
          <span class="font-weight-medium" :class="account.balance < 0 ? 'text-error' : ''">
            {{ formatAmount(account.balance) }}
          </span>
        </template>
      </v-list-item>
    </v-list>
    <v-card-text v-else class="text-medium-emphasis">
      Aucun compte pour l'instant.
      <v-btn :to="{ name: 'accounts' }" variant="text" color="primary">Créer un compte</v-btn>
    </v-card-text>
  </v-card>
</template>
