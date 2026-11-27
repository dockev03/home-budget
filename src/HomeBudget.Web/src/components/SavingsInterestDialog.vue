<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { mdiChevronLeft, mdiChevronRight, mdiDeleteOutline, mdiPencilOutline } from '@mdi/js'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import type { Account } from '@/stores/reference'
import { formatAmount, formatDate, todayIso } from '@/utils/format'
import { formatRate, parseRate } from '@/utils/loans'
import { fortnightLabel, rateText, type SavingsInterest, type SavingsRate } from '@/utils/savings'
import { required } from '@/utils/rules'

/**
 * Rémunération d'un livret (A9) : taux passés et programmés, intérêts acquis par quinzaine et estimation au
 * 31 décembre. Les montants sont indicatifs : l'opération réelle de la banque arrive par l'import ou la saisie.
 */
const props = defineProps<{ account: Account | null }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ changed: [] }>()

const auth = useAuthStore()
const thisYear = Number(todayIso().slice(0, 4))
const year = ref(thisYear)
const interest = ref<SavingsInterest | null>(null)
const rates = ref<SavingsRate[]>([])
const loading = ref(false)
const error = ref<string | null>(null)

const editing = ref<SavingsRate | null>(null)
const effectiveFrom = ref(todayIso())
const rate = ref('')
const valid = ref(false)
const saving = ref(false)
// Suppression en deux temps : un premier clic arme le bouton, le second supprime.
const pendingDelete = ref<string | null>(null)

const title = computed(() => (props.account ? `Rémunération · ${props.account.name}` : 'Rémunération'))
const rateRule = (v: string) => parseRate(v) !== null || 'Taux de 0 à 100 %, 4 décimales au plus.'

function reset() {
  pendingDelete.value = null
  editing.value = null
  effectiveFrom.value = todayIso()
  rate.value = ''
}

async function loadInterest() {
  if (!props.account) return
  interest.value = await unwrap(
    api.GET('/api/accounts/{accountId}/interest', {
      params: { path: { accountId: props.account.id }, query: { year: year.value } },
    }),
  )
}

async function load() {
  if (!props.account) return
  loading.value = true
  error.value = null
  try {
    const accountId = props.account.id
    const [list] = await Promise.all([
      unwrap(api.GET('/api/accounts/{accountId}/savings-rates', { params: { path: { accountId } } })),
      loadInterest(),
    ])
    rates.value = list
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

watch(visible, (open) => {
  if (!open) return
  reset()
  year.value = thisYear
  interest.value = null
  rates.value = []
  void load()
})

async function changeYear(delta: number) {
  year.value += delta
  error.value = null
  try {
    await loadInterest()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

function edit(r: SavingsRate) {
  editing.value = r
  effectiveFrom.value = r.effectiveFrom
  rate.value = String(r.annualRate).replace('.', ',')
}

async function save() {
  if (!valid.value || !props.account) return
  saving.value = true
  error.value = null
  const body = { effectiveFrom: effectiveFrom.value, annualRate: parseRate(rate.value) ?? 0 }
  const accountId = props.account.id
  try {
    if (editing.value) {
      await unwrap(
        api.PUT('/api/accounts/{accountId}/savings-rates/{id}', {
          params: { path: { accountId, id: editing.value.id } },
          headers: ifMatch(editing.value.version),
          body,
        }),
      )
    } else {
      await unwrap(
        api.POST('/api/accounts/{accountId}/savings-rates', { params: { path: { accountId } }, body }),
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

async function remove(r: SavingsRate) {
  if (!props.account) return
  if (pendingDelete.value !== r.id) {
    pendingDelete.value = r.id
    return
  }
  pendingDelete.value = null
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/accounts/{accountId}/savings-rates/{id}', {
        params: { path: { accountId: props.account.id, id: r.id } },
        headers: ifMatch(r.version),
      }),
    )
    if (editing.value?.id === r.id) reset()
    await load()
    emit('changed')
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  }
}
</script>

<template>
  <v-dialog v-model="visible" max-width="640" scrollable>
    <v-card :title="title" data-testid="savings-interest">
      <v-card-subtitle class="text-wrap">
        Calcul par quinzaine (du 1er au 15 et du 16 à la fin du mois) : un dépôt rapporte à partir de la
        quinzaine suivante, un retrait ne rapporte plus dès le début de la sienne.
      </v-card-subtitle>
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <v-progress-linear v-if="loading" indeterminate class="mb-2" />

        <template v-if="interest">
          <div class="d-flex align-center mb-2">
            <v-btn
              :icon="mdiChevronLeft"
              variant="text"
              size="small"
              aria-label="Année précédente"
              @click="changeYear(-1)"
            />
            <span class="text-subtitle-1 font-weight-medium mx-2">{{ interest.year }}</span>
            <v-btn
              :icon="mdiChevronRight"
              variant="text"
              size="small"
              aria-label="Année suivante"
              @click="changeYear(1)"
            />
          </div>
          <v-row dense class="mb-2">
            <v-col cols="6">
              <div class="text-caption text-medium-emphasis">
                Intérêts acquis au {{ formatDate(interest.asOf) }}
              </div>
              <div class="text-h6 font-weight-bold" data-testid="interest-earned">
                {{ formatAmount(interest.earned) }}
              </div>
            </v-col>
            <v-col cols="6">
              <div class="text-caption text-medium-emphasis">Estimés au 31/12</div>
              <div class="text-h6" data-testid="interest-projected">
                {{ formatAmount(interest.projected) }}
              </div>
            </v-col>
          </v-row>
          <p class="text-body-2 mb-1">Taux : {{ rateText(interest) }}.</p>
          <p class="text-body-2 text-medium-emphasis mb-4">
            Estimation avec les opérations à venir saisies et les taux programmés. Les intérêts sont versés
            par la banque au 31 décembre : ils ne comptent qu'une fois l'opération enregistrée sur le compte.
          </p>

          <v-expansion-panels variant="accordion" class="mb-4">
            <v-expansion-panel title="Détail par quinzaine">
              <v-expansion-panel-text>
                <v-table density="compact" data-testid="interest-fortnights">
                  <thead>
                    <tr>
                      <th>Quinzaine</th>
                      <th class="text-right">Solde rémunéré</th>
                      <th class="text-right">Taux</th>
                      <th class="text-right">Intérêts</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr
                      v-for="f in interest.fortnights"
                      :key="f.start"
                      :class="{ 'text-medium-emphasis': !f.isEnded }"
                    >
                      <td>{{ fortnightLabel(f) }}</td>
                      <td class="text-right">{{ formatAmount(f.base) }}</td>
                      <td class="text-right">{{ formatRate(f.annualRate) }}</td>
                      <td class="text-right">{{ formatAmount(f.interest) }}</td>
                    </tr>
                  </tbody>
                </v-table>
              </v-expansion-panel-text>
            </v-expansion-panel>
          </v-expansion-panels>
        </template>

        <div class="text-subtitle-2 mb-2">Taux</div>
        <v-form v-if="auth.canWrite" v-model="valid" class="mb-2" @submit.prevent="save">
          <v-row dense>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="effectiveFrom"
                label="À partir du"
                type="date"
                hint="Une date future programme un changement."
                persistent-hint
                :rules="[required]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="rate"
                label="Taux annuel"
                suffix="%"
                inputmode="decimal"
                :rules="[required, rateRule]"
              />
            </v-col>
          </v-row>
          <div class="d-flex justify-end ga-2 mt-2">
            <v-btn v-if="editing" @click="reset">Annuler la modification</v-btn>
            <v-btn color="primary" variant="flat" type="submit" :loading="saving" :disabled="!valid">
              {{ editing ? 'Enregistrer' : 'Ajouter le taux' }}
            </v-btn>
          </div>
        </v-form>

        <v-list v-if="rates.length" density="compact" data-testid="savings-rates">
          <v-list-item v-for="r in rates" :key="r.id" :title="formatRate(r.annualRate)">
            <v-list-item-subtitle>
              À partir du {{ formatDate(r.effectiveFrom) }}
              <v-chip v-if="r.isScheduled" size="x-small" color="primary" class="ml-1">Programmé</v-chip>
            </v-list-item-subtitle>
            <template v-if="auth.canWrite" #append>
              <v-btn
                :icon="mdiPencilOutline"
                variant="text"
                size="small"
                :aria-label="`Modifier le taux du ${formatDate(r.effectiveFrom)}`"
                @click="edit(r)"
              />
              <v-btn
                :icon="mdiDeleteOutline"
                :variant="pendingDelete === r.id ? 'flat' : 'text'"
                :color="pendingDelete === r.id ? 'error' : undefined"
                size="small"
                :aria-label="
                  pendingDelete === r.id
                    ? `Confirmer la suppression du taux du ${formatDate(r.effectiveFrom)}`
                    : `Supprimer le taux du ${formatDate(r.effectiveFrom)}`
                "
                @click="remove(r)"
              />
            </template>
          </v-list-item>
        </v-list>
        <p v-else-if="!loading" class="text-medium-emphasis">
          Aucun taux saisi : ajoutez le taux actuel du livret (par exemple 1,7 % pour le Livret A depuis le
          01/08/2026).
        </p>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="visible = false">Fermer</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
