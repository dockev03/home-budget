<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiPencilOutline, mdiPlus } from '@mdi/js'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore } from '@/stores/reference'
import { formatSignedAmount, parseAmount, roundCents, todayIso } from '@/utils/format'
import { required } from '@/utils/rules'
import { describeWhen, type Scenario, type ScenarioEvent } from '@/utils/scenarios'

/**
 * Scénarios « et si… » (F5) : des événements fictifs, nommés, qui s'ajoutent à la prévision du tableau de bord
 * dans une série à part. Ils ne créent jamais d'opération et ne touchent ni au budget ni aux totaux.
 */
const auth = useAuthStore()
const reference = useReferenceStore()

const scenarios = ref<Scenario[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

// Scénario : nom et activation.
const scenarioDialog = ref(false)
const editingScenario = ref<Scenario | null>(null)
const name = ref('')
const isActive = ref(true)
const scenarioValid = ref(false)

// Événement fictif.
const eventDialog = ref(false)
const eventScenario = ref<Scenario | null>(null)
const editingEvent = ref<ScenarioEvent | null>(null)
const label = ref('')
const accountId = ref<string | null>(null)
const categoryId = ref<string | null>(null)
const amount = ref('')
const isIncome = ref(false)
const date = ref(todayIso())
const monthly = ref(false)
const endDate = ref('')
const eventValid = ref(false)

const saving = ref(false)
const deleting = ref(false)
const confirmDelete = ref(false)
const formError = ref<string | null>(null)

// Seuls les comptes de la prévision font bouger la courbe.
const accountItems = computed(() =>
  reference.accounts
    .filter((a) => (!a.isArchived && a.includeInForecast) || a.id === accountId.value)
    .map((a) => ({ value: a.id, title: a.name })),
)
const categoryItems = computed(() =>
  reference.categories
    .filter((c) => !c.isArchived || c.id === categoryId.value)
    .map((c) => ({ value: c.id, title: reference.categoryLabel(c.id) })),
)
const amountRule = (v: string) => {
  const value = parseAmount(v)
  return (value !== null && value > 0) || 'Montant positif, 2 décimales au plus.'
}
const endRule = (v: string) =>
  !monthly.value || !v || v >= date.value || 'La fin doit suivre la première date.'

function accountName(id: string): string {
  return reference.accounts.find((a) => a.id === id)?.name ?? 'Compte masqué'
}

function describeEvent(e: ScenarioEvent): string {
  const category = e.categoryId ? ` · ${reference.categoryLabel(e.categoryId)}` : ''
  return `${describeWhen(e)} · ${accountName(e.accountId)}${category}`
}

async function load() {
  loading.value = true
  try {
    await reference.load()
    scenarios.value = await unwrap(api.GET('/api/scenarios'))
    error.value = null
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

function openScenario(s: Scenario | null) {
  if (!auth.canWrite) return
  editingScenario.value = s
  name.value = s?.name ?? ''
  isActive.value = s?.isActive ?? true
  formError.value = null
  confirmDelete.value = false
  scenarioDialog.value = true
}

async function saveScenario() {
  if (!scenarioValid.value) return
  saving.value = true
  formError.value = null
  const body = { name: name.value.trim(), isActive: isActive.value }
  try {
    if (editingScenario.value) {
      await unwrap(
        api.PUT('/api/scenarios/{id}', {
          params: { path: { id: editingScenario.value.id } },
          headers: ifMatch(editingScenario.value.version),
          body,
        }),
      )
    } else {
      await unwrap(api.POST('/api/scenarios', { body }))
    }
    scenarioDialog.value = false
    await load()
  } catch (e) {
    formError.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function toggleActive(s: Scenario, value: boolean | null) {
  try {
    await unwrap(
      api.PUT('/api/scenarios/{id}', {
        params: { path: { id: s.id } },
        headers: ifMatch(s.version),
        body: { name: s.name, isActive: !!value },
      }),
    )
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  }
  await load()
}

async function removeScenario() {
  const s = editingScenario.value
  if (!s) return
  deleting.value = true
  formError.value = null
  try {
    await unwrap(
      api.DELETE('/api/scenarios/{id}', { params: { path: { id: s.id } }, headers: ifMatch(s.version) }),
    )
    scenarioDialog.value = false
    await load()
  } catch (e) {
    formError.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}

function openEvent(s: Scenario, e: ScenarioEvent | null) {
  if (!auth.canWrite) return
  eventScenario.value = s
  editingEvent.value = e
  label.value = e?.label ?? ''
  accountId.value = e?.accountId ?? accountItems.value[0]?.value ?? null
  categoryId.value = e?.categoryId ?? null
  amount.value = e ? String(Math.abs(e.amount)).replace('.', ',') : ''
  isIncome.value = (e?.amount ?? -1) > 0
  date.value = e?.date ?? todayIso()
  monthly.value = e?.frequency === 'Monthly'
  endDate.value = e?.endDate ?? ''
  formError.value = null
  confirmDelete.value = false
  eventDialog.value = true
}

async function saveEvent() {
  const s = eventScenario.value
  if (!s || !eventValid.value || !accountId.value) return
  saving.value = true
  formError.value = null
  const value = roundCents(parseAmount(amount.value)!)
  const body = {
    label: label.value.trim(),
    accountId: accountId.value,
    categoryId: categoryId.value,
    amount: isIncome.value ? value : -value,
    date: date.value,
    frequency: monthly.value ? ('Monthly' as const) : ('Once' as const),
    endDate: monthly.value && endDate.value ? endDate.value : null,
  }
  try {
    if (editingEvent.value) {
      await unwrap(
        api.PUT('/api/scenarios/{id}/events/{eventId}', {
          params: { path: { id: s.id, eventId: editingEvent.value.id } },
          headers: ifMatch(editingEvent.value.version),
          body,
        }),
      )
    } else {
      await unwrap(api.POST('/api/scenarios/{id}/events', { params: { path: { id: s.id } }, body }))
    }
    eventDialog.value = false
    await load()
  } catch (e) {
    formError.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function removeEvent() {
  const s = eventScenario.value
  const e = editingEvent.value
  if (!s || !e) return
  deleting.value = true
  formError.value = null
  try {
    await unwrap(
      api.DELETE('/api/scenarios/{id}/events/{eventId}', {
        params: { path: { id: s.id, eventId: e.id } },
        headers: ifMatch(e.version),
      }),
    )
    eventDialog.value = false
    await load()
  } catch (err) {
    formError.value = err instanceof ApiError ? err.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="d-flex align-center flex-wrap ga-2 mb-2">
    <h1 class="text-h5 font-weight-bold mr-auto">Scénarios</h1>
    <v-btn v-if="auth.canWrite" :prepend-icon="mdiPlus" color="primary" @click="openScenario(null)">
      Nouveau scénario
    </v-btn>
  </div>
  <p class="text-body-2 text-medium-emphasis mb-4">
    « Et si… » : des dépenses ou des rentrées fictives, ajoutées à la prévision du tableau de bord dans une
    courbe à part. Elles ne créent aucune opération et ne touchent ni au budget ni aux totaux.
  </p>

  <v-progress-linear v-if="loading" indeterminate color="primary" class="mb-2" />
  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <v-card v-if="!loading && scenarios.length === 0" class="pa-6 text-center text-medium-emphasis">
    Aucun scénario. Créez-en un, par exemple « Nouvelle voiture », puis ajoutez ses événements.
  </v-card>

  <v-card v-for="s in scenarios" :key="s.id" class="mb-4" data-testid="scenario">
    <v-card-title class="d-flex align-center flex-wrap ga-2">
      <span class="mr-auto">{{ s.name }}</span>
      <v-switch
        :model-value="s.isActive"
        color="primary"
        label="Actif"
        density="compact"
        hide-details
        :disabled="!auth.canWrite"
        @update:model-value="toggleActive(s, $event)"
      />
      <v-btn
        v-if="auth.canWrite"
        :icon="mdiPencilOutline"
        variant="text"
        size="small"
        :aria-label="`Modifier le scénario ${s.name}`"
        @click="openScenario(s)"
      />
    </v-card-title>
    <v-list lines="two">
      <v-list-item
        v-for="e in s.events"
        :key="e.id"
        :title="e.label"
        :subtitle="describeEvent(e)"
        :disabled="!auth.canWrite"
        @click="openEvent(s, e)"
      >
        <template #append>
          <span class="font-weight-medium text-no-wrap" :class="e.amount < 0 ? '' : 'text-success'">
            {{ formatSignedAmount(e.amount) }}{{ e.frequency === 'Monthly' ? ' / mois' : '' }}
          </span>
        </template>
      </v-list-item>
      <v-list-item v-if="s.events.length === 0" class="text-medium-emphasis" title="Aucun événement." />
    </v-list>
    <v-card-actions v-if="auth.canWrite">
      <v-btn :prepend-icon="mdiPlus" variant="text" color="primary" @click="openEvent(s, null)">
        Ajouter un événement
      </v-btn>
    </v-card-actions>
  </v-card>

  <v-dialog v-model="scenarioDialog" max-width="460">
    <v-card :title="editingScenario ? 'Modifier le scénario' : 'Nouveau scénario'">
      <v-card-text>
        <v-alert v-if="formError" type="error" variant="tonal" class="mb-4" :text="formError" />
        <v-form v-model="scenarioValid" @submit.prevent="saveScenario">
          <v-text-field v-model="name" label="Nom" maxlength="100" :rules="[required]" v-autofocus />
          <v-switch
            v-model="isActive"
            color="primary"
            label="Actif : affiché par défaut sur le tableau de bord"
            hide-details
          />
          <button type="submit" hidden />
        </v-form>
      </v-card-text>
      <v-card-actions>
        <template v-if="editingScenario">
          <v-btn v-if="!confirmDelete" color="error" @click="confirmDelete = true">Supprimer</v-btn>
          <v-btn v-else color="error" variant="flat" :loading="deleting" @click="removeScenario">
            Supprimer avec ses événements
          </v-btn>
        </template>
        <v-spacer />
        <v-btn @click="scenarioDialog = false">Annuler</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="saving"
          :disabled="!scenarioValid"
          @click="saveScenario"
          >Enregistrer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>

  <v-dialog v-model="eventDialog" max-width="520">
    <v-card :title="editingEvent ? 'Modifier l’événement' : 'Nouvel événement'">
      <v-card-subtitle v-if="eventScenario">{{ eventScenario.name }}</v-card-subtitle>
      <v-card-text>
        <v-alert v-if="formError" type="error" variant="tonal" class="mb-4" :text="formError" />
        <v-form v-model="eventValid" @submit.prevent="saveEvent">
          <v-text-field v-model="label" label="Libellé" maxlength="100" :rules="[required]" v-autofocus />
          <v-btn-toggle v-model="isIncome" mandatory color="primary" density="compact" class="mb-4">
            <v-btn :value="false">Dépense</v-btn>
            <v-btn :value="true">Rentrée</v-btn>
          </v-btn-toggle>
          <v-text-field
            v-model="amount"
            label="Montant"
            suffix="€"
            inputmode="decimal"
            :rules="[amountRule]"
          />
          <v-select
            v-model="accountId"
            :items="accountItems"
            label="Compte"
            hint="Seuls les comptes inclus dans la prévision sont proposés."
            persistent-hint
            :rules="[required]"
            class="mb-2"
          />
          <v-autocomplete
            v-model="categoryId"
            :items="categoryItems"
            label="Catégorie (facultative, pour mémoire)"
            clearable
          />
          <v-switch v-model="monthly" color="primary" label="Chaque mois" hide-details class="mb-2" />
          <v-row dense>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="date"
                :label="monthly ? 'Première fois le' : 'Date'"
                type="date"
                :rules="[required]"
              />
            </v-col>
            <v-col v-if="monthly" cols="12" sm="6">
              <v-text-field v-model="endDate" label="Jusqu’au (facultatif)" type="date" :rules="[endRule]" />
            </v-col>
          </v-row>
          <button type="submit" hidden />
        </v-form>
      </v-card-text>
      <v-card-actions>
        <template v-if="editingEvent">
          <v-btn v-if="!confirmDelete" color="error" @click="confirmDelete = true">Supprimer</v-btn>
          <v-btn v-else color="error" variant="flat" :loading="deleting" @click="removeEvent">
            Confirmer la suppression
          </v-btn>
        </template>
        <v-spacer />
        <v-btn @click="eventDialog = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!eventValid" @click="saveEvent"
          >Enregistrer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
