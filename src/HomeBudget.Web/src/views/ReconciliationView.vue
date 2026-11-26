<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { mdiLockOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore } from '@/stores/reference'
import { formatAmount, formatDate, formatSignedAmount, parseAmount, todayIso } from '@/utils/format'
import { reconciliationState } from '@/utils/reconciliation'

type Preview = Schemas['ReconciliationPreviewDto']
type Reconciliation = Schemas['ReconciliationDto']

const auth = useAuthStore()
const reference = useReferenceStore()

const accountId = ref<string | null>(null)
const date = ref(todayIso())
const balance = ref('')
const preview = ref<Preview | null>(null)
const history = ref<Reconciliation[]>([])
const computing = ref(false)
const saving = ref(false)
const cancelling = ref(false)
const confirmCancel = ref(false)
const error = ref<string | null>(null)
const success = ref<string | null>(null)

const accountItems = computed(() => reference.activeAccounts.map((a) => ({ value: a.id, title: a.name })))
const state = computed(() => (preview.value ? reconciliationState(preview.value) : null))
const statementBalance = computed(() => parseAmount(balance.value))

// Autre compte, date ou solde : le calcul n'est plus valable.
watch([accountId, date, balance], () => {
  preview.value = null
  confirmCancel.value = false
})
watch(accountId, () => void loadHistory())

async function loadHistory() {
  if (!accountId.value) return
  try {
    history.value = await unwrap(
      api.GET('/api/accounts/{accountId}/reconciliations', {
        params: { path: { accountId: accountId.value } },
      }),
    )
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

async function compute() {
  if (!accountId.value || statementBalance.value === null) return
  computing.value = true
  error.value = null
  success.value = null
  try {
    preview.value = await unwrap(
      api.GET('/api/accounts/{accountId}/reconciliations/preview', {
        params: {
          path: { accountId: accountId.value },
          query: { date: date.value, statementBalance: statementBalance.value },
        },
      }),
    )
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Calcul impossible.'
  } finally {
    computing.value = false
  }
}

async function reconcile() {
  if (!accountId.value || !preview.value || !state.value?.canReconcile) return
  saving.value = true
  error.value = null
  try {
    const result = await unwrap(
      api.POST('/api/accounts/{accountId}/reconciliations', {
        params: { path: { accountId: accountId.value } },
        body: { date: preview.value.date, statementBalance: preview.value.statementBalance },
      }),
    )
    success.value = `Compte rapproché au ${formatDate(result.date)} : ${result.transactionCount} opération(s) verrouillée(s).`
    preview.value = null
    balance.value = ''
    await loadHistory()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Rapprochement impossible.'
  } finally {
    saving.value = false
  }
}

async function cancelLast(r: Reconciliation) {
  if (!accountId.value) return
  if (!confirmCancel.value) {
    confirmCancel.value = true
    return
  }
  cancelling.value = true
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/accounts/{accountId}/reconciliations/{id}', {
        params: { path: { accountId: accountId.value, id: r.id } },
        headers: ifMatch(r.version),
      }),
    )
    success.value = `Rapprochement du ${formatDate(r.date)} annulé : ses opérations sont déverrouillées.`
    confirmCancel.value = false
    await loadHistory()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Annulation impossible.'
  } finally {
    cancelling.value = false
  }
}

onMounted(async () => {
  try {
    await reference.load()
    accountId.value = reference.activeAccounts[0]?.id ?? null
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
})
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Rapprochement bancaire</h1>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
  <v-alert
    v-if="success"
    type="success"
    variant="tonal"
    class="mb-4"
    :text="success"
    closable
    @click:close="success = null"
  />

  <v-card class="mb-4">
    <v-card-text>
      <p class="text-body-2 text-medium-emphasis mb-4">
        Saisissez le solde indiqué par votre relevé. Il est comparé au solde des opérations pointées à la même
        date ; sans écart, ces opérations sont rapprochées et ne peuvent plus être modifiées.
      </p>
      <v-row dense>
        <v-col cols="12" md="4">
          <v-select v-model="accountId" :items="accountItems" label="Compte" />
        </v-col>
        <v-col cols="6" md="4">
          <v-text-field v-model="date" label="Date du relevé" type="date" :max="todayIso()" />
        </v-col>
        <v-col cols="6" md="4">
          <v-text-field v-model="balance" label="Solde du relevé" suffix="€" inputmode="decimal" />
        </v-col>
      </v-row>
    </v-card-text>
    <v-card-actions>
      <v-spacer />
      <v-btn
        color="primary"
        variant="tonal"
        :loading="computing"
        :disabled="!accountId || statementBalance === null"
        @click="compute"
        >Calculer l’écart</v-btn
      >
    </v-card-actions>
  </v-card>

  <v-card v-if="preview && state" class="mb-4" data-testid="reconciliation-preview">
    <v-card-text>
      <v-row dense>
        <v-col cols="6" md="3">
          <div class="text-caption text-medium-emphasis">Solde du relevé</div>
          <div class="text-subtitle-1">{{ formatAmount(preview.statementBalance) }}</div>
        </v-col>
        <v-col cols="6" md="3">
          <div class="text-caption text-medium-emphasis">Solde pointé</div>
          <div class="text-subtitle-1">{{ formatAmount(preview.clearedBalance) }}</div>
        </v-col>
        <v-col cols="6" md="3">
          <div class="text-caption text-medium-emphasis">Écart</div>
          <div class="text-subtitle-1" :class="preview.difference === 0 ? 'text-success' : 'text-error'">
            {{ formatSignedAmount(preview.difference) }}
          </div>
        </v-col>
        <v-col cols="6" md="3">
          <div class="text-caption text-medium-emphasis">Non pointées à cette date</div>
          <div class="text-subtitle-1">{{ preview.unclearedCount }}</div>
        </v-col>
      </v-row>
      <v-alert
        :type="state.canReconcile ? 'success' : 'warning'"
        variant="tonal"
        class="mt-2"
        :text="state.message"
      />
    </v-card-text>
    <v-card-actions>
      <v-btn
        v-if="preview.unclearedCount"
        variant="text"
        :to="{ name: 'transactions', query: { accountId, cleared: 'false', to: preview.date } }"
        >Voir les opérations non pointées</v-btn
      >
      <v-spacer />
      <v-btn
        v-if="auth.canWrite"
        color="primary"
        variant="flat"
        :prepend-icon="mdiLockOutline"
        :loading="saving"
        :disabled="!state.canReconcile"
        @click="reconcile"
        >Rapprocher</v-btn
      >
    </v-card-actions>
  </v-card>

  <v-card v-if="history.length" title="Rapprochements du compte">
    <v-list density="compact">
      <v-list-item
        v-for="(r, index) in history"
        :key="r.id"
        :title="`Relevé du ${formatDate(r.date)} · ${formatAmount(r.statementBalance)}`"
        :subtitle="`${r.transactionCount} opération(s) rapprochée(s)`"
      >
        <template v-if="index === 0 && auth.canWrite" #append>
          <v-btn
            :color="confirmCancel ? 'error' : undefined"
            :variant="confirmCancel ? 'flat' : 'text'"
            :loading="cancelling"
            @click="cancelLast(r)"
            >{{ confirmCancel ? 'Confirmer l’annulation' : 'Annuler' }}</v-btn
          >
        </template>
      </v-list-item>
    </v-list>
  </v-card>
</template>
