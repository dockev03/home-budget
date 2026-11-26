<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import type { Account } from '@/stores/reference'
import { formatAmount, formatDate, parseAmount, todayIso } from '@/utils/format'
import { durationLabel, formatRate, loanSummary, parseRate, repaidPercent, type Loan } from '@/utils/loans'
import { required } from '@/utils/rules'

/**
 * Crédit (A8) : conditions d'un compte de crédit (capital, taux, durée, assurance), tableau d'amortissement et capital
 * restant dû.
 */
const props = defineProps<{ account: Account | null }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ changed: [] }>()

const auth = useAuthStore()
const loan = ref<Loan | null>(null)
const loading = ref(false)
const editing = ref(false)
const saving = ref(false)
const valid = ref(false)
const error = ref<string | null>(null)
const confirmDelete = ref(false)
const showAll = ref(false)
const form = ref({ principal: '', rate: '', months: '', firstPaymentDate: todayIso(), insurance: '0' })

const today = todayIso()
const title = computed(() => (props.account ? `Crédit · ${props.account.name}` : 'Crédit'))
const amountRule = (v: string) => {
  const amount = parseAmount(v)
  return (amount !== null && amount >= 0) || 'Montant positif, 2 décimales au plus.'
}
const principalRule = (v: string) => (parseAmount(v) ?? 0) > 0 || 'Capital positif requis.'
const rateRule = (v: string) => parseRate(v) !== null || 'Taux de 0 à 100 %, 4 décimales au plus.'
const monthsRule = (v: string) => (/^\d+$/.test(v.trim()) && +v >= 1 && +v <= 600) || 'De 1 à 600 mois.'

/** Les 12 mensualités autour d'aujourd'hui, ou tout le tableau. */
const rows = computed(() => {
  const schedule = loan.value?.schedule ?? []
  if (showAll.value) return schedule
  const next = schedule.findIndex((i) => i.date > today)
  const from = Math.max(0, (next === -1 ? schedule.length : next) - 3)
  return schedule.slice(from, from + 12)
})

function fill(l: Loan | null) {
  form.value = l
    ? {
        principal: String(l.principal).replace('.', ','),
        rate: String(l.annualRate).replace('.', ','),
        months: String(l.durationMonths),
        firstPaymentDate: l.firstPaymentDate,
        insurance: String(l.monthlyInsurance).replace('.', ','),
      }
    : {
        principal: String(Math.abs(props.account?.initialBalance ?? 0)).replace('.', ','),
        rate: '',
        months: '',
        firstPaymentDate: todayIso(),
        insurance: '0',
      }
}

function cancelEdit() {
  editing.value = false
  fill(loan.value)
}

async function load() {
  if (!props.account) return
  loading.value = true
  error.value = null
  const { data, response } = await api.GET('/api/accounts/{accountId}/loan', {
    params: { path: { accountId: props.account.id } },
  })
  loading.value = false
  if (response.status === 404) {
    loan.value = null
  } else if (data) {
    loan.value = data
  } else {
    error.value = 'Chargement impossible.'
  }
  editing.value = !loan.value && auth.canWrite
  fill(loan.value)
}

watch(visible, (open) => {
  if (!open) return
  loan.value = null
  confirmDelete.value = false
  showAll.value = false
  void load()
})

async function save() {
  if (!valid.value || !props.account) return
  saving.value = true
  error.value = null
  try {
    loan.value = await unwrap(
      api.PUT('/api/accounts/{accountId}/loan', {
        params: { path: { accountId: props.account.id } },
        headers: loan.value ? ifMatch(loan.value.version) : {},
        body: {
          principal: parseAmount(form.value.principal) ?? 0,
          annualRate: parseRate(form.value.rate) ?? 0,
          durationMonths: Number(form.value.months),
          firstPaymentDate: form.value.firstPaymentDate,
          monthlyInsurance: parseAmount(form.value.insurance) ?? 0,
        },
      }),
    )
    editing.value = false
    emit('changed')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove() {
  if (!props.account || !loan.value) return
  if (!confirmDelete.value) {
    confirmDelete.value = true
    return
  }
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/accounts/{accountId}/loan', {
        params: { path: { accountId: props.account.id } },
        headers: ifMatch(loan.value.version),
      }),
    )
    loan.value = null
    confirmDelete.value = false
    editing.value = true
    fill(null)
    emit('changed')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  }
}
</script>

<template>
  <v-dialog v-model="visible" max-width="720" scrollable>
    <v-card :title="title" data-testid="loan">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <v-progress-linear v-if="loading" indeterminate class="mb-2" />

        <template v-if="loan && !editing">
          <v-row dense class="mb-2">
            <v-col cols="6" sm="3">
              <div class="text-caption text-medium-emphasis">Capital restant dû</div>
              <div class="text-h6 font-weight-bold" data-testid="loan-remaining">
                {{ formatAmount(loan.remainingPrincipal) }}
              </div>
            </v-col>
            <v-col cols="6" sm="3">
              <div class="text-caption text-medium-emphasis">Mensualité</div>
              <div class="text-h6" data-testid="loan-payment">
                {{ formatAmount(loan.paymentWithInsurance) }}
              </div>
            </v-col>
            <v-col cols="6" sm="3">
              <div class="text-caption text-medium-emphasis">Coût total</div>
              <div class="text-h6">{{ formatAmount(loan.totalCost) }}</div>
            </v-col>
            <v-col cols="6" sm="3">
              <div class="text-caption text-medium-emphasis">Remboursé</div>
              <div class="text-h6">{{ loan.paidCount }} / {{ loan.durationMonths }}</div>
            </v-col>
          </v-row>
          <v-progress-linear
            :model-value="repaidPercent(loan)"
            color="primary"
            height="8"
            rounded
            class="mb-2"
          />
          <p class="text-body-2 mb-1">{{ loanSummary(loan) }}.</p>
          <p class="text-body-2 text-medium-emphasis mb-4">
            {{ formatAmount(loan.principal) }} à {{ formatRate(loan.annualRate) }} sur
            {{ durationLabel(loan.durationMonths) }} · intérêts {{ formatAmount(loan.totalInterest) }} ·
            assurance {{ formatAmount(loan.totalInsurance) }}
          </p>

          <v-table density="compact" data-testid="loan-schedule">
            <thead>
              <tr>
                <th>N°</th>
                <th>Date</th>
                <th class="text-right">Mensualité</th>
                <th class="text-right">Intérêts</th>
                <th class="text-right">Capital</th>
                <th class="text-right">Restant dû</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="i in rows" :key="i.number" :class="{ 'text-medium-emphasis': i.date <= today }">
                <td>{{ i.number }}</td>
                <td>{{ formatDate(i.date) }}</td>
                <td class="text-right">{{ formatAmount(i.payment + i.insurance) }}</td>
                <td class="text-right">{{ formatAmount(i.interest) }}</td>
                <td class="text-right">{{ formatAmount(i.principal) }}</td>
                <td class="text-right">{{ formatAmount(i.remaining) }}</td>
              </tr>
            </tbody>
          </v-table>
          <v-btn variant="text" size="small" class="mt-2" @click="showAll = !showAll">
            {{
              showAll
                ? 'Autour d’aujourd’hui seulement'
                : `Tout le tableau (${loan.schedule.length} mensualités)`
            }}
          </v-btn>
        </template>

        <v-form v-else-if="editing" v-model="valid" @submit.prevent="save">
          <p class="text-body-2 text-medium-emphasis mb-4">
            Mensualités constantes : le tableau d'amortissement et le capital restant dû en découlent.
          </p>
          <v-row dense>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="form.principal"
                label="Capital emprunté"
                suffix="€"
                inputmode="decimal"
                :rules="[required, principalRule, amountRule]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="form.rate"
                label="Taux annuel"
                suffix="%"
                inputmode="decimal"
                :rules="[required, rateRule]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="form.months"
                label="Durée"
                suffix="mois"
                inputmode="numeric"
                :rules="[required, monthsRule]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="form.firstPaymentDate"
                label="Première mensualité"
                type="date"
                :rules="[required]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="form.insurance"
                label="Assurance par mois"
                suffix="€"
                inputmode="decimal"
                :rules="[required, amountRule]"
              />
            </v-col>
          </v-row>
          <button type="submit" hidden />
        </v-form>

        <p v-else-if="!loading" class="text-medium-emphasis">Aucune condition de crédit saisie.</p>
      </v-card-text>
      <v-card-actions>
        <template v-if="auth.canWrite && loan && !editing">
          <v-btn
            :color="confirmDelete ? 'error' : undefined"
            :variant="confirmDelete ? 'flat' : 'text'"
            @click="remove"
          >
            {{ confirmDelete ? 'Confirmer la suppression' : 'Supprimer' }}
          </v-btn>
          <v-btn variant="text" @click="editing = true">Modifier</v-btn>
        </template>
        <v-spacer />
        <v-btn v-if="editing && loan" @click="cancelEdit">Annuler</v-btn>
        <v-btn
          v-if="editing"
          color="primary"
          variant="flat"
          :loading="saving"
          :disabled="!valid"
          @click="save"
        >
          Enregistrer
        </v-btn>
        <v-btn @click="visible = false">Fermer</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
