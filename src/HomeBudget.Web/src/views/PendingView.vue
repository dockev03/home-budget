<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { mdiCalendarArrowRight, mdiCheck, mdiDebugStepOver } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { usePendingStore } from '@/stores/pending'
import { useReferenceStore } from '@/stores/reference'
import { formatDate, formatSignedAmount, parseAmount, roundCents, toIso } from '@/utils/format'

type Pending = Schemas['PendingOccurrenceDto']
type Action = 'confirm' | 'postpone'

const auth = useAuthStore()
const reference = useReferenceStore()
const pendingStore = usePendingStore()
const error = ref<string | null>(null)
const busy = ref<string | null>(null)

// Saisie en cours sur une échéance : montant ajusté (confirmation) ou nouvelle date (report).
const editing = ref<{ id: string; action: Action } | null>(null)
const amount = ref('')
const date = ref('')

function tomorrowIso(): string {
  const d = new Date()
  d.setDate(d.getDate() + 1)
  return toIso(d)
}

/** Montant dans le sens de l'opération (un virement est saisi en positif). */
const shown = (p: Pending) => (p.toAccountId ? -p.amount : p.amount)

function account(p: Pending): string {
  const from = reference.accountById.get(p.accountId)?.name ?? 'Compte inconnu'
  return p.toAccountId
    ? `${from} → ${reference.accountById.get(p.toAccountId)?.name ?? 'Compte inconnu'}`
    : from
}

function open(p: Pending, action: Action) {
  editing.value = { id: p.id, action }
  amount.value = String(Math.abs(p.amount)).replace('.', ',')
  date.value = action === 'confirm' ? p.dueDate : tomorrowIso()
}

async function run(p: Pending, call: () => Promise<unknown>) {
  busy.value = p.id
  error.value = null
  try {
    await call()
    editing.value = null
    await Promise.all([pendingStore.load(), reference.loadAccounts()])
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Action impossible.'
  } finally {
    busy.value = null
  }
}

function confirm(p: Pending) {
  const value = parseAmount(amount.value)
  if (value === null || value <= 0) {
    error.value = 'Montant invalide.'
    return
  }
  // Même sens que l'échéance prévue : une dépense reste négative.
  const signed = roundCents(p.amount < 0 ? -value : value)
  return run(p, () =>
    unwrap(
      api.POST('/api/recurrences/occurrences/{id}/confirm', {
        params: { path: { id: p.id } },
        body: { amount: signed, date: date.value || null },
      }),
    ),
  )
}

const skip = (p: Pending) =>
  run(p, () => unwrap(api.POST('/api/recurrences/occurrences/{id}/skip', { params: { path: { id: p.id } } })))

const postpone = (p: Pending) =>
  run(p, () =>
    unwrap(
      api.POST('/api/recurrences/occurrences/{id}/postpone', {
        params: { path: { id: p.id } },
        body: { date: date.value },
      }),
    ),
  )

onMounted(async () => {
  try {
    await Promise.all([reference.load(), pendingStore.load()])
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
})
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">À valider</h1>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <v-card v-if="pendingStore.items.length === 0" :loading="pendingStore.loading">
    <v-card-text class="text-medium-emphasis">Aucune échéance à valider.</v-card-text>
  </v-card>

  <v-card v-for="p in pendingStore.items" :key="p.id" class="mb-3">
    <v-card-item>
      <v-card-title class="d-flex align-center">
        {{ p.label }}
        <v-spacer />
        <span :class="shown(p) > 0 ? 'text-success' : ''">{{ formatSignedAmount(shown(p)) }}</span>
      </v-card-title>
      <v-card-subtitle>
        Prévue le {{ formatDate(p.scheduledDate) }}
        <template v-if="p.dueDate !== p.scheduledDate"> · reportée au {{ formatDate(p.dueDate) }}</template>
        · {{ account(p) }}
        <template v-if="p.payeeName"> · {{ p.payeeName }}</template>
        <template v-if="!p.toAccountId"> · {{ reference.categoryLabel(p.categoryId) }}</template>
      </v-card-subtitle>
    </v-card-item>

    <v-card-text v-if="editing?.id === p.id" class="pt-0">
      <v-row dense>
        <v-col v-if="editing.action === 'confirm'" cols="12" sm="6">
          <v-text-field
            v-model="amount"
            label="Montant réel"
            suffix="€"
            inputmode="decimal"
            density="compact"
            hide-details
          />
        </v-col>
        <v-col cols="12" sm="6">
          <v-text-field
            v-model="date"
            :label="editing.action === 'confirm' ? 'Date de l’opération' : 'Reporter au'"
            type="date"
            density="compact"
            hide-details
            :min="editing.action === 'postpone' ? tomorrowIso() : undefined"
          />
        </v-col>
      </v-row>
    </v-card-text>

    <v-card-actions v-if="auth.canWrite">
      <template v-if="editing?.id === p.id">
        <v-spacer />
        <v-btn @click="editing = null">Annuler</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="busy === p.id"
          @click="editing.action === 'confirm' ? confirm(p) : postpone(p)"
        >
          {{ editing.action === 'confirm' ? 'Confirmer' : 'Reporter' }}
        </v-btn>
      </template>
      <template v-else>
        <v-btn :prepend-icon="mdiDebugStepOver" :loading="busy === p.id" @click="skip(p)">Sauter</v-btn>
        <v-btn :prepend-icon="mdiCalendarArrowRight" @click="open(p, 'postpone')">Reporter</v-btn>
        <v-spacer />
        <v-btn :prepend-icon="mdiCheck" color="primary" variant="tonal" @click="open(p, 'confirm')"
          >Valider</v-btn
        >
      </template>
    </v-card-actions>
  </v-card>
</template>
