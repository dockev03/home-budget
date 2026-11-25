<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { mdiAlertOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import BudgetDialog from '@/components/BudgetDialog.vue'
import PeriodNavigator from '@/components/PeriodNavigator.vue'
import { useAuthStore } from '@/stores/auth'
import { budgetModeLabel, gaugeValue, remainingLabel, statusColor, type BudgetLine } from '@/utils/budget'
import { formatAmount, formatSignedAmount } from '@/utils/format'
import { iconPath } from '@/utils/icons'
import { categoryKinds } from '@/utils/labels'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const budget = ref<Schemas['BudgetDto'] | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const dialog = ref(false)
const editing = ref<BudgetLine | null>(null)

const periodKey = computed(() => (route.params.period as string | undefined) || null)

const sections = computed(() =>
  categoryKinds.map((kind) => {
    const lines = budget.value?.categories.filter((c) => c.kind === kind.value) ?? []
    return { ...kind, lines }
  }),
)

const summaryRows = computed(() => {
  const b = budget.value
  if (!b) return []
  return [
    { title: 'Revenus', budgeted: b.budgeted.income, actual: b.actual.income },
    { title: 'Dépenses fixes', budgeted: -b.budgeted.fixed, actual: -b.actual.fixed },
    { title: 'Dépenses variables', budgeted: -b.budgeted.variable, actual: -b.actual.variable },
    { title: 'Épargne', budgeted: -b.budgeted.savings, actual: -b.actual.savings },
  ]
})

async function load() {
  loading.value = true
  error.value = null
  try {
    let key = periodKey.value
    if (!key) key = (await unwrap(api.GET('/api/periods/current'))).key
    budget.value = await unwrap(api.GET('/api/budgets/{periodKey}', { params: { path: { periodKey: key } } }))
  } catch (e) {
    budget.value = null
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

function goTo(key: string | null) {
  router.push(key ? { name: 'budget', params: { period: key } } : { name: 'budget' })
}

function edit(line: BudgetLine) {
  if (!auth.canWrite) return
  editing.value = line
  dialog.value = true
}

watch(periodKey, load, { immediate: true })
</script>

<template>
  <div class="d-flex align-center flex-wrap ga-2 mb-4">
    <h1 class="text-h5 font-weight-bold mr-auto">Budget</h1>
    <PeriodNavigator :period="budget?.period ?? null" @go="goTo" />
  </div>

  <v-progress-linear v-if="loading" indeterminate color="primary" class="mb-2" />
  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <template v-if="budget">
    <!-- Récapitulatif (D8) : Restant = Revenus − Fixes − Variables − Épargne. -->
    <v-card class="mb-4" title="Récapitulatif">
      <v-table density="comfortable">
        <thead>
          <tr>
            <th></th>
            <th class="text-right">Budget</th>
            <th class="text-right">Réel</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in summaryRows" :key="row.title">
            <td>{{ row.title }}</td>
            <td class="text-right">{{ formatSignedAmount(row.budgeted) }}</td>
            <td class="text-right">{{ formatSignedAmount(row.actual) }}</td>
          </tr>
          <tr class="font-weight-bold">
            <td>Restant</td>
            <td class="text-right" :class="budget.budgeted.remaining < 0 ? 'text-error' : ''">
              {{ formatAmount(budget.budgeted.remaining) }}
            </td>
            <td class="text-right" :class="budget.actual.remaining < 0 ? 'text-error' : ''">
              {{ formatAmount(budget.actual.remaining) }}
            </td>
          </tr>
        </tbody>
      </v-table>
      <v-alert
        v-if="budget.uncategorized !== 0"
        type="warning"
        variant="tonal"
        density="compact"
        class="ma-4"
        :icon="mdiAlertOutline"
      >
        {{ formatSignedAmount(budget.uncategorized) }} d'opérations sans catégorie, hors récapitulatif.
      </v-alert>
    </v-card>

    <v-card v-for="section in sections" :key="section.value" class="mb-4" :title="section.title">
      <v-list lines="three">
        <v-list-item
          v-for="line in section.lines"
          :key="line.categoryId"
          :class="{ 'pl-12': line.parentId, 'text-disabled': line.isArchived }"
          :disabled="!auth.canWrite"
          :aria-label="`Budget ${line.name}`"
          @click="edit(line)"
        >
          <template #prepend>
            <v-avatar :color="line.color ?? 'primary'" variant="tonal" :size="line.parentId ? 28 : 36">
              <v-icon :icon="iconPath(line.icon)" :size="line.parentId ? 16 : 20" />
            </v-avatar>
          </template>
          <v-list-item-title class="d-flex">
            <span class="text-truncate mr-2">{{ line.name }}</span>
            <v-spacer />
            <span class="text-no-wrap">
              <strong>{{ formatAmount(line.actual) }}</strong>
              <span class="text-medium-emphasis"> / {{ formatAmount(line.budgeted) }}</span>
            </span>
          </v-list-item-title>
          <v-progress-linear
            :model-value="gaugeValue(line)"
            :color="statusColor(line.status)"
            height="8"
            rounded
            class="my-2"
            :aria-label="`Consommation du budget ${line.name}`"
          />
          <v-list-item-subtitle class="d-flex">
            <span class="text-truncate mr-2">{{ budgetModeLabel(line) }}</span>
            <v-spacer />
            <span class="text-no-wrap" :class="line.status === 'Exceeded' ? 'text-error' : ''">
              {{ remainingLabel(line) }}
            </span>
          </v-list-item-subtitle>
        </v-list-item>
        <v-list-item
          v-if="section.lines.length === 0"
          title="Aucune catégorie de ce type."
          class="text-medium-emphasis"
        />
      </v-list>
    </v-card>
  </template>

  <BudgetDialog v-model="dialog" :line="editing" :income-base="budget?.incomeBase ?? 0" @saved="load" />
</template>
