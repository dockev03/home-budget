<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiPencilOutline, mdiPlus, mdiShieldCheckOutline } from '@mdi/js'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import AppearancePicker from '@/components/AppearancePicker.vue'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore } from '@/stores/reference'
import { formatAmount, parseAmount, todayIso } from '@/utils/format'
import { iconPath } from '@/utils/icons'
import { required } from '@/utils/rules'
import {
  contributionText,
  goalSource,
  goalStatusColor,
  goalStatusTitle,
  monthsCoveredColor,
  monthsCoveredText,
  type EmergencyFund,
  type SavingsGoal,
} from '@/utils/savings'

/**
 * Objectifs d'épargne (J1) avec leur versement mensuel conseillé (J2), et épargne de précaution (J3). Un objectif
 * se mesure au solde d'un compte (par exemple un livret ouvert pour un enfant, J4) ou au cumul d'une catégorie
 * d'épargne depuis une date.
 */
const auth = useAuthStore()
const reference = useReferenceStore()

const goals = ref<SavingsGoal[]>([])
const fund = ref<EmergencyFund | null>(null)
const showArchived = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)

const dialog = ref(false)
const editing = ref<SavingsGoal | null>(null)
const form = ref({
  name: '',
  targetAmount: '',
  targetDate: '',
  source: 'account' as 'account' | 'category',
  accountId: null as string | null,
  categoryId: null as string | null,
  startDate: todayIso(),
  icon: null as string | null,
  color: null as string | null,
  isArchived: false,
})
const valid = ref(false)
const saving = ref(false)
const deleting = ref(false)
const confirmDelete = ref(false)
const formError = ref<string | null>(null)

const accountItems = computed(() =>
  reference.accounts
    .filter((a) => !a.isArchived || a.id === form.value.accountId)
    .map((a) => ({ value: a.id, title: a.name })),
)
const categoryItems = computed(() =>
  reference.categories
    .filter((c) => c.kind === 'Savings' && (!c.isArchived || c.id === form.value.categoryId))
    .map((c) => ({ value: c.id, title: reference.categoryLabel(c.id) })),
)
const amountRule = (v: string) => {
  const value = parseAmount(v)
  return (value !== null && value > 0) || 'Montant positif, 2 décimales au plus.'
}

const accountName = (id: string) => reference.accounts.find((a) => a.id === id)?.name ?? 'compte masqué'
const categoryName = (id: string) => reference.categoryLabel(id)

async function load() {
  loading.value = true
  try {
    await reference.load()
    const [list, emergency] = await Promise.all([
      unwrap(api.GET('/api/savings-goals', { params: { query: { includeArchived: showArchived.value } } })),
      unwrap(api.GET('/api/emergency-fund')),
    ])
    goals.value = list
    fund.value = emergency
    error.value = null
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

function open(goal: SavingsGoal | null) {
  if (!auth.canWrite) return
  editing.value = goal
  formError.value = null
  confirmDelete.value = false
  form.value = goal
    ? {
        name: goal.name,
        targetAmount: String(goal.targetAmount).replace('.', ','),
        targetDate: goal.targetDate ?? '',
        source: goal.accountId ? 'account' : 'category',
        accountId: goal.accountId,
        categoryId: goal.categoryId,
        startDate: goal.startDate,
        icon: goal.icon,
        color: goal.color,
        isArchived: goal.isArchived,
      }
    : {
        name: '',
        targetAmount: '',
        targetDate: '',
        source: 'account',
        accountId: null,
        categoryId: null,
        startDate: todayIso(),
        icon: 'piggy-bank',
        color: null,
        isArchived: false,
      }
  dialog.value = true
}

async function save() {
  if (!valid.value) return
  saving.value = true
  formError.value = null
  const f = form.value
  const byAccount = f.source === 'account'
  const body = {
    name: f.name.trim(),
    targetAmount: parseAmount(f.targetAmount) ?? 0,
    targetDate: f.targetDate || null,
    accountId: byAccount ? f.accountId : null,
    categoryId: byAccount ? null : f.categoryId,
    startDate: byAccount ? null : f.startDate || null,
    icon: f.icon,
    color: f.color,
    isArchived: f.isArchived,
  }
  try {
    if (editing.value) {
      await unwrap(
        api.PUT('/api/savings-goals/{id}', {
          params: { path: { id: editing.value.id } },
          headers: ifMatch(editing.value.version),
          body,
        }),
      )
    } else {
      await unwrap(api.POST('/api/savings-goals', { body }))
    }
    dialog.value = false
    await load()
  } catch (e) {
    formError.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove() {
  if (!editing.value) return
  deleting.value = true
  formError.value = null
  try {
    await unwrap(
      api.DELETE('/api/savings-goals/{id}', {
        params: { path: { id: editing.value.id } },
        headers: ifMatch(editing.value.version),
      }),
    )
    dialog.value = false
    await load()
  } catch (e) {
    formError.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="d-flex align-center flex-wrap ga-2 mb-4">
    <h1 class="text-h5 font-weight-bold mr-auto">Objectifs d’épargne</h1>
    <v-btn v-if="auth.canWrite" :prepend-icon="mdiPlus" color="primary" @click="open(null)">
      Nouvel objectif
    </v-btn>
  </div>

  <v-progress-linear v-if="loading" indeterminate color="primary" class="mb-2" />
  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <!-- J3 : épargne de précaution. -->
  <v-card v-if="fund" class="mb-4" data-testid="emergency-fund">
    <v-card-item>
      <template #prepend>
        <v-avatar :color="monthsCoveredColor(fund.monthsCovered)" variant="tonal">
          <v-icon :icon="mdiShieldCheckOutline" />
        </v-avatar>
      </template>
      <v-card-title>Épargne de précaution : {{ formatAmount(fund.amount) }}</v-card-title>
      <v-card-subtitle class="text-wrap">{{ monthsCoveredText(fund) }}</v-card-subtitle>
    </v-card-item>
    <v-card-text class="text-body-2 text-medium-emphasis pt-0">
      <template v-if="fund.averageMonths > 0">
        Dépenses moyennes : {{ formatAmount(fund.monthlyExpenses) }} par mois, sur les
        {{ fund.averageMonths }} dernières périodes clôturées.
      </template>
      <template v-if="fund.accounts.length">
        Comptes : {{ fund.accounts.map((a) => a.name).join(', ') }}.
      </template>
      <template v-else>
        Aucun compte marqué « épargne de précaution » : cochez-le dans la fiche d'un compte.
      </template>
    </v-card-text>
  </v-card>

  <v-card v-if="!loading && goals.length === 0" class="pa-6 text-center text-medium-emphasis">
    Aucun objectif. Créez-en un, par exemple « Vacances d'été » ou « Livret de l'enfant ».
  </v-card>

  <v-row>
    <v-col v-for="goal in goals" :key="goal.id" cols="12" md="6">
      <v-card class="h-100" :class="{ 'text-disabled': goal.isArchived }" data-testid="savings-goal">
        <v-card-item>
          <template #prepend>
            <v-avatar :color="goal.color ?? 'primary'" variant="tonal">
              <v-icon :icon="iconPath(goal.icon ?? 'piggy-bank')" />
            </v-avatar>
          </template>
          <v-card-title class="text-wrap">{{ goal.name }}</v-card-title>
          <v-card-subtitle class="text-wrap">{{
            goalSource(goal, accountName, categoryName)
          }}</v-card-subtitle>
          <template #append>
            <v-chip :color="goalStatusColor(goal.status)" size="small" variant="tonal">
              {{ goal.isArchived ? 'Archivé' : goalStatusTitle(goal.status) }}
            </v-chip>
            <v-btn
              v-if="auth.canWrite"
              :icon="mdiPencilOutline"
              variant="text"
              size="small"
              :aria-label="`Modifier l'objectif ${goal.name}`"
              @click="open(goal)"
            />
          </template>
        </v-card-item>
        <v-card-text>
          <div class="d-flex justify-space-between text-body-2 mb-1">
            <span>{{ formatAmount(goal.saved) }} sur {{ formatAmount(goal.targetAmount) }}</span>
            <span class="font-weight-medium">{{ goal.percent }} %</span>
          </div>
          <v-progress-linear
            :model-value="goal.percent"
            :color="goalStatusColor(goal.status)"
            height="10"
            rounded
            :aria-label="`Avancement de ${goal.name}`"
          />
          <p class="text-body-2 mt-2 mb-0" data-testid="goal-advice">{{ contributionText(goal) }}</p>
        </v-card-text>
      </v-card>
    </v-col>
  </v-row>

  <v-switch
    v-model="showArchived"
    label="Afficher les objectifs archivés"
    color="primary"
    hide-details
    class="mt-2"
    @update:model-value="load"
  />

  <v-dialog v-model="dialog" max-width="560" scrollable>
    <v-card :title="editing ? 'Modifier l’objectif' : 'Nouvel objectif'">
      <v-card-text>
        <v-alert v-if="formError" type="error" variant="tonal" class="mb-4" :text="formError" />
        <v-form v-model="valid" @submit.prevent="save">
          <v-text-field v-model="form.name" label="Nom" maxlength="100" :rules="[required]" v-autofocus />
          <v-row dense>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="form.targetAmount"
                label="Montant cible"
                suffix="€"
                inputmode="decimal"
                :rules="[required, amountRule]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="form.targetDate"
                label="Date cible (facultative)"
                type="date"
                clearable
                hint="Sert au versement mensuel conseillé."
                persistent-hint
              />
            </v-col>
          </v-row>
          <v-radio-group v-model="form.source" label="Épargne mesurée par" inline hide-details class="mb-2">
            <v-radio label="Le solde d'un compte" value="account" />
            <v-radio label="Une catégorie d'épargne" value="category" />
          </v-radio-group>
          <v-select
            v-if="form.source === 'account'"
            v-model="form.accountId"
            :items="accountItems"
            label="Compte"
            :rules="[required]"
            hint="Par exemple un livret dédié : son solde est l'épargne réunie."
            persistent-hint
          />
          <v-row v-else dense>
            <v-col cols="12" sm="7">
              <v-select
                v-model="form.categoryId"
                :items="categoryItems"
                label="Catégorie d'épargne"
                :rules="[required]"
                no-data-text="Aucune catégorie de type épargne."
              />
            </v-col>
            <v-col cols="12" sm="5">
              <v-text-field
                v-model="form.startDate"
                label="Cumul depuis le"
                type="date"
                :rules="[required]"
              />
            </v-col>
          </v-row>
          <AppearancePicker v-model:icon="form.icon" v-model:color="form.color" class="mt-2" />
          <v-switch
            v-if="editing"
            v-model="form.isArchived"
            label="Archivé (masqué de la liste et du tableau de bord)"
            color="primary"
            hide-details
          />
          <button type="submit" hidden />
        </v-form>
      </v-card-text>
      <v-card-actions>
        <template v-if="editing">
          <v-btn v-if="!confirmDelete" color="error" @click="confirmDelete = true">Supprimer</v-btn>
          <v-btn v-else color="error" variant="flat" :loading="deleting" @click="remove">
            Confirmer la suppression
          </v-btn>
        </template>
        <v-spacer />
        <v-btn @click="dialog = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save">
          Enregistrer
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
