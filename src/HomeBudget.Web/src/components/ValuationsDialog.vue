<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { mdiDeleteOutline, mdiPencilOutline } from '@mdi/js'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import type { Account } from '@/stores/reference'
import { formatAmount, formatDate, formatSignedAmount, parseAmount, todayIso } from '@/utils/format'
import { required } from '@/utils/rules'
import type { Valuation } from '@/utils/savings'

/**
 * Valorisations d'un compte d'investissement (A7) : l'historique des valeurs saisies. Le solde du compte part de
 * la dernière valorisation et y ajoute les opérations datées après elle.
 */
const props = defineProps<{ account: Account | null }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ changed: [] }>()

const auth = useAuthStore()
const valuations = ref<Valuation[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

const editing = ref<Valuation | null>(null)
const date = ref(todayIso())
const value = ref('')
const note = ref('')
const valid = ref(false)
const saving = ref(false)
// Suppression en deux temps : un premier clic arme le bouton, le second supprime.
const pendingDelete = ref<string | null>(null)

const today = todayIso()
const valueRule = (v: string) => parseAmount(v) !== null || 'Montant invalide (2 décimales au plus).'
const dateRule = (v: string) =>
  (!!v && v <= today && (!props.account || v >= props.account.initialDate)) ||
  "Date entre l'ouverture du compte et aujourd'hui."
const title = computed(() => (props.account ? `Valorisations · ${props.account.name}` : 'Valorisations'))

function reset() {
  pendingDelete.value = null
  editing.value = null
  date.value = todayIso()
  value.value = ''
  note.value = ''
}

async function load() {
  if (!props.account) return
  loading.value = true
  error.value = null
  try {
    valuations.value = await unwrap(
      api.GET('/api/accounts/{accountId}/valuations', { params: { path: { accountId: props.account.id } } }),
    )
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

watch(visible, (open) => {
  if (!open) return
  reset()
  valuations.value = []
  void load()
})

function edit(v: Valuation) {
  editing.value = v
  date.value = v.date
  value.value = String(v.value).replace('.', ',')
  note.value = v.note ?? ''
}

async function save() {
  if (!valid.value || !props.account) return
  saving.value = true
  error.value = null
  const body = { date: date.value, value: parseAmount(value.value) ?? 0, note: note.value || null }
  const accountId = props.account.id
  try {
    if (editing.value) {
      await unwrap(
        api.PUT('/api/accounts/{accountId}/valuations/{id}', {
          params: { path: { accountId, id: editing.value.id } },
          headers: ifMatch(editing.value.version),
          body,
        }),
      )
    } else {
      await unwrap(
        api.POST('/api/accounts/{accountId}/valuations', { params: { path: { accountId } }, body }),
      )
    }
    reset()
    await load()
    emit('changed')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove(v: Valuation) {
  if (!props.account) return
  if (pendingDelete.value !== v.id) {
    pendingDelete.value = v.id
    return
  }
  pendingDelete.value = null
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/accounts/{accountId}/valuations/{id}', {
        params: { path: { accountId: props.account.id, id: v.id } },
        headers: ifMatch(v.version),
      }),
    )
    if (editing.value?.id === v.id) reset()
    await load()
    emit('changed')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  }
}
</script>

<template>
  <v-dialog v-model="visible" max-width="560" scrollable>
    <v-card :title="title" data-testid="valuations">
      <v-card-subtitle class="text-wrap">
        Le solde part de la dernière valeur saisie, et y ajoute les opérations datées après elle.
      </v-card-subtitle>
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <v-form v-if="auth.canWrite" v-model="valid" class="mb-2" @submit.prevent="save">
          <v-row dense>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="date"
                label="Date"
                type="date"
                :max="today"
                :rules="[required, dateRule]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="value"
                label="Valeur"
                suffix="€"
                inputmode="decimal"
                :rules="[required, valueRule]"
              />
            </v-col>
          </v-row>
          <v-text-field v-model="note" label="Note (facultative)" maxlength="200" />
          <div class="d-flex justify-end ga-2">
            <v-btn v-if="editing" @click="reset">Annuler la modification</v-btn>
            <v-btn color="primary" variant="flat" type="submit" :loading="saving" :disabled="!valid">
              {{ editing ? 'Enregistrer' : 'Ajouter la valeur' }}
            </v-btn>
          </div>
        </v-form>

        <v-progress-linear v-if="loading" indeterminate class="mb-2" />
        <v-list v-if="valuations.length" lines="two" density="compact">
          <v-list-item v-for="v in valuations" :key="v.id" :title="formatAmount(v.value)">
            <v-list-item-subtitle>
              {{ formatDate(v.date) }}
              <span v-if="v.change != null" :class="v.change < 0 ? 'text-error' : 'text-success'">
                · {{ formatSignedAmount(v.change) }}
              </span>
              <span v-if="v.note"> · {{ v.note }}</span>
            </v-list-item-subtitle>
            <template v-if="auth.canWrite" #append>
              <v-btn
                :icon="mdiPencilOutline"
                variant="text"
                size="small"
                :aria-label="`Modifier la valeur du ${formatDate(v.date)}`"
                @click="edit(v)"
              />
              <v-btn
                :icon="mdiDeleteOutline"
                :variant="pendingDelete === v.id ? 'flat' : 'text'"
                :color="pendingDelete === v.id ? 'error' : undefined"
                size="small"
                :aria-label="
                  pendingDelete === v.id
                    ? `Confirmer la suppression de la valeur du ${formatDate(v.date)}`
                    : `Supprimer la valeur du ${formatDate(v.date)}`
                "
                @click="remove(v)"
              />
            </template>
          </v-list-item>
        </v-list>
        <p v-else-if="!loading" class="text-medium-emphasis">
          Aucune valeur saisie : le solde suit le solde initial et les opérations.
        </p>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="visible = false">Fermer</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
