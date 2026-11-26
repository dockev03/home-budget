<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiClose } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useReferenceStore } from '@/stores/reference'
import { parseAmount, roundCents, todayIso } from '@/utils/format'
import { categoryKindTitle } from '@/utils/labels'
import {
  frequencyUnits,
  recurrenceEnds,
  recurrenceModes,
  type FrequencyUnit,
  type RecurrenceEnd,
  type RecurrenceMode,
} from '@/utils/recurrence'
import { required } from '@/utils/rules'

type Recurrence = Schemas['RecurrenceDto']
type Kind = 'expense' | 'income' | 'transfer'

const props = defineProps<{ recurrence: Recurrence | null }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [] }>()

const reference = useReferenceStore()
const { smAndDown } = useDisplay()

const kind = ref<Kind>('expense')
const label = ref('')
const amount = ref('')
const accountId = ref<string | null>(null)
const toAccountId = ref<string | null>(null)
const payeeName = ref<string | null>(null)
const categoryId = ref<string | null>(null)
const notes = ref('')
const mode = ref<RecurrenceMode>('Automatic')
const startDate = ref(todayIso())
const unit = ref<FrequencyUnit>('Month')
const interval = ref('1')
const end = ref<RecurrenceEnd>('Never')
const endDate = ref<string | null>(null)
const count = ref('')
const isInstallment = ref(false)
const tolerance = ref('0')
const toleranceUnit = ref<'Amount' | 'Percent'>('Amount')
const isSubscription = ref(false)

const valid = ref(false)
const saving = ref(false)
const deleting = ref(false)
const confirmDelete = ref(false)
const error = ref<string | null>(null)
const payeeItems = ref<string[]>([])

const isEdit = computed(() => !!props.recurrence)

const accounts = computed(() =>
  reference.accounts
    .filter((a) => !a.isArchived || a.id === accountId.value || a.id === toAccountId.value)
    .map((a) => ({ value: a.id, title: a.name })),
)

/** Catégories actives : épargne seule pour un virement, celles du type de l'opération en premier sinon. */
const categoryItems = computed(() => {
  const preferred = kind.value === 'income' ? ['Income'] : ['Fixed', 'Variable', 'Savings']
  const rank = (k: string) => (preferred.includes(k) ? preferred.indexOf(k) : 10)
  return reference.categories
    .filter((c) => !c.isArchived || c.id === categoryId.value)
    .filter((c) => kind.value !== 'transfer' || c.kind === 'Savings')
    .map((c) => ({ value: c.id, title: reference.categoryLabel(c.id), kind: c.kind }))
    .sort((a, b) => rank(a.kind) - rank(b.kind))
})

const amountRule = (v: string) => {
  const value = parseAmount(v)
  if (value === null) return 'Montant invalide (2 décimales au plus).'
  return value > 0 || 'Montant positif attendu.'
}
const toleranceRule = (v: string) => {
  const value = parseAmount(v)
  if (value === null || value < 0) return 'Tolérance invalide.'
  return toleranceUnit.value !== 'Percent' || value <= 100 || '100 % au plus.'
}
const intervalRule = (v: string) => (/^\d{1,3}$/.test(v) && Number(v) >= 1 ? true : 'De 1 à 999.')
const differentAccount = (v: string | null) => v !== accountId.value || 'Choisissez deux comptes différents.'
const endDateRule = (v: string | null) =>
  end.value !== 'OnDate' || (!!v && v >= startDate.value) || 'La fin doit suivre le départ.'

watch(isInstallment, (installment) => {
  if (installment) end.value = 'AfterCount'
})
watch(kind, (value) => {
  if (value === 'transfer') payeeName.value = null
  if (categoryId.value && !categoryItems.value.some((c) => c.value === categoryId.value)) {
    categoryId.value = null
  }
})

async function reset() {
  error.value = null
  confirmDelete.value = false
  await reference.load()
  const r = props.recurrence
  if (!r) {
    kind.value = 'expense'
    label.value = ''
    amount.value = ''
    accountId.value = reference.activeAccounts[0]?.id ?? null
    toAccountId.value = null
    payeeName.value = null
    categoryId.value = null
    notes.value = ''
    mode.value = 'Automatic'
    startDate.value = todayIso()
    unit.value = 'Month'
    interval.value = '1'
    end.value = 'Never'
    endDate.value = null
    count.value = ''
    isInstallment.value = false
    tolerance.value = '0'
    toleranceUnit.value = 'Amount'
    isSubscription.value = false
    return
  }
  kind.value = r.toAccountId ? 'transfer' : r.amount > 0 ? 'income' : 'expense'
  label.value = r.label
  amount.value = String(Math.abs(r.amount)).replace('.', ',')
  accountId.value = r.accountId
  toAccountId.value = r.toAccountId
  payeeName.value = r.payeeName
  categoryId.value = r.categoryId
  notes.value = r.notes ?? ''
  mode.value = r.mode
  startDate.value = r.startDate
  unit.value = r.unit
  interval.value = String(r.interval)
  end.value = r.end
  endDate.value = r.endDate
  count.value = r.count ? String(r.count) : ''
  isInstallment.value = r.isInstallment
  tolerance.value = String(r.tolerance).replace('.', ',')
  toleranceUnit.value = r.toleranceUnit
  isSubscription.value = r.isSubscription
}

watch(visible, (open) => {
  if (open) void reset()
})

let searchTimer: ReturnType<typeof setTimeout> | undefined
function searchPayees(text: string | null) {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(async () => {
    const { data } = await api.GET('/api/payees', {
      params: { query: { search: text ?? undefined, limit: 10 } },
    })
    payeeItems.value = (data ?? []).map((p) => p.name)
  }, 200)
}

async function save() {
  if (!valid.value || !accountId.value) return
  saving.value = true
  error.value = null
  const value = parseAmount(amount.value)!
  const body = {
    label: label.value.trim(),
    accountId: accountId.value,
    toAccountId: kind.value === 'transfer' ? toAccountId.value : null,
    payeeName: kind.value === 'transfer' ? null : payeeName.value || null,
    categoryId: categoryId.value,
    amount: roundCents(kind.value === 'expense' ? -value : value),
    notes: notes.value || null,
    mode: mode.value,
    startDate: startDate.value,
    unit: unit.value,
    interval: Number(interval.value),
    end: end.value,
    endDate: end.value === 'OnDate' ? endDate.value : null,
    count: end.value === 'AfterCount' ? Number(count.value) : null,
    isInstallment: isInstallment.value,
    tolerance: roundCents(parseAmount(tolerance.value) ?? 0),
    toleranceUnit: toleranceUnit.value,
    isSubscription: kind.value !== 'transfer' && isSubscription.value,
  }
  try {
    if (props.recurrence) {
      await unwrap(
        api.PUT('/api/recurrences/{id}', {
          params: { path: { id: props.recurrence.id } },
          headers: ifMatch(props.recurrence.version),
          body,
        }),
      )
    } else {
      await unwrap(api.POST('/api/recurrences', { body }))
    }
    await reference.loadAccounts()
    visible.value = false
    emit('saved')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove() {
  const r = props.recurrence
  if (!r) return
  deleting.value = true
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/recurrences/{id}', { params: { path: { id: r.id } }, headers: ifMatch(r.version) }),
    )
    visible.value = false
    emit('saved')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}
</script>

<template>
  <v-dialog v-model="visible" :fullscreen="smAndDown" max-width="640" scrollable>
    <v-card>
      <v-toolbar density="comfortable" color="surface">
        <v-btn :icon="mdiClose" aria-label="Fermer" @click="visible = false" />
        <v-toolbar-title>{{ isEdit ? 'Modifier la récurrence' : 'Nouvelle récurrence' }}</v-toolbar-title>
      </v-toolbar>

      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

        <v-btn-toggle
          v-model="kind"
          mandatory
          divided
          variant="outlined"
          color="primary"
          class="mb-4 w-100"
          :disabled="isEdit"
        >
          <v-btn value="expense" class="flex-grow-1">Dépense</v-btn>
          <v-btn value="income" class="flex-grow-1">Revenu</v-btn>
          <v-btn value="transfer" class="flex-grow-1">Virement</v-btn>
        </v-btn-toggle>

        <v-form v-model="valid" @submit.prevent="save">
          <v-text-field v-model="label" label="Libellé" maxlength="200" :rules="[required]" autofocus />

          <v-row dense>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="amount"
                :label="isInstallment ? 'Montant de chaque échéance' : 'Montant'"
                suffix="€"
                inputmode="decimal"
                :rules="[amountRule]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field v-model="startDate" label="Première échéance" type="date" :rules="[required]" />
            </v-col>
          </v-row>

          <v-select
            v-model="accountId"
            :items="accounts"
            :label="kind === 'transfer' ? 'Depuis le compte' : 'Compte'"
            :rules="[required]"
          />
          <v-select
            v-if="kind === 'transfer'"
            v-model="toAccountId"
            :items="accounts"
            label="Vers le compte"
            :rules="[required, differentAccount]"
          />
          <v-combobox
            v-else
            v-model="payeeName"
            :items="payeeItems"
            label="Tiers"
            clearable
            @update:search="searchPayees"
            @focus="searchPayees(payeeName)"
          />

          <v-autocomplete
            v-model="categoryId"
            :items="categoryItems"
            :label="kind === 'transfer' ? 'Catégorie d’épargne (facultatif)' : 'Catégorie'"
            clearable
            no-data-text="Aucune catégorie de type épargne"
          >
            <template #item="{ props: itemProps, item }">
              <v-list-item v-bind="itemProps" :subtitle="categoryKindTitle(item.kind)" />
            </template>
          </v-autocomplete>

          <v-row v-if="kind !== 'transfer'" dense>
            <v-col cols="7" sm="6">
              <v-text-field
                v-model="tolerance"
                label="Tolérance de montant"
                inputmode="decimal"
                hint="Écart admis pour reconnaître l’opération dans un relevé importé"
                persistent-hint
                :rules="[toleranceRule]"
              />
            </v-col>
            <v-col cols="5" sm="3">
              <v-select
                v-model="toleranceUnit"
                :items="[
                  { value: 'Amount', title: '€' },
                  { value: 'Percent', title: '%' },
                ]"
                label="Unité"
              />
            </v-col>
          </v-row>
          <v-checkbox
            v-if="kind !== 'transfer'"
            v-model="isSubscription"
            label="Abonnement (compté dans la vue « Abonnements »)"
            hide-details
            class="mb-2"
          />

          <v-row dense align="center">
            <v-col cols="5" sm="3">
              <v-text-field v-model="interval" label="Tous les" inputmode="numeric" :rules="[intervalRule]" />
            </v-col>
            <v-col cols="7" sm="4">
              <v-select v-model="unit" :items="frequencyUnits" label="Fréquence" />
            </v-col>
            <v-col cols="12" sm="5">
              <v-select v-model="end" :items="recurrenceEnds" label="Fin" :disabled="isInstallment" />
            </v-col>
          </v-row>

          <v-text-field
            v-if="end === 'OnDate'"
            v-model="endDate"
            label="Dernière échéance au plus tard le"
            type="date"
            :rules="[endDateRule]"
          />
          <v-text-field
            v-if="end === 'AfterCount'"
            v-model="count"
            label="Nombre d’échéances"
            inputmode="numeric"
            :rules="[intervalRule]"
          />

          <v-checkbox
            v-if="kind !== 'transfer'"
            v-model="isInstallment"
            label="Paiement en plusieurs fois (libellé numéroté : « Montre 1/4 »)"
            hide-details
            class="mb-2"
          />

          <v-radio-group v-model="mode" label="Création des opérations" hide-details class="mb-2">
            <v-radio v-for="m in recurrenceModes" :key="m.value" :value="m.value">
              <template #label>
                <div>
                  <div>{{ m.title }}</div>
                  <div class="text-caption text-medium-emphasis">{{ m.hint }}</div>
                </div>
              </template>
            </v-radio>
          </v-radio-group>

          <v-textarea v-model="notes" label="Note" rows="2" auto-grow maxlength="2000" />
          <p class="text-caption text-medium-emphasis">
            Les échéances antérieures à aujourd’hui ne créent pas d’opération. Une modification ne change pas
            les échéances déjà traitées.
          </p>
          <button type="submit" hidden />
        </v-form>
      </v-card-text>

      <v-card-actions>
        <template v-if="isEdit">
          <v-btn v-if="!confirmDelete" color="error" @click="confirmDelete = true">Supprimer</v-btn>
          <v-btn v-else color="error" variant="flat" :loading="deleting" @click="remove"
            >Confirmer la suppression</v-btn
          >
        </template>
        <v-spacer />
        <v-btn @click="visible = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save"
          >Enregistrer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
