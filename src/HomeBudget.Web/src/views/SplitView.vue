<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { mdiArrowRight, mdiCogOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import PeriodNavigator from '@/components/PeriodNavigator.vue'
import { useAuthStore } from '@/stores/auth'
import { formatAmount, formatSignedAmount, parseAmount } from '@/utils/format'
import { balanceColor, balanceText, sharesTotal, splitModes, splitModeTitle, type Split } from '@/utils/split'

/**
 * Répartition des dépenses communes (K4) : ce que chaque membre a apporté (dépenses payées depuis les comptes dont il
 * est titulaire, versements aux comptes joints), sa part, son solde et les remboursements conseillés. Seuls les
 * comptes partagés comptent (R8).
 */
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const split = ref<Split | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

const settingsOpen = ref(false)
const settings = ref<Schemas['SplitSettingsDto'] | null>(null)
const mode = ref<Schemas['SplitMode']>('Equal')
const shares = ref<{ userId: string; name: string; percent: string }[]>([])
const savingSettings = ref(false)
const settingsError = ref<string | null>(null)

const periodKey = computed(() => (route.params.period as string | undefined) || null)
const total = computed(() => sharesTotal(shares.value.map((s) => parseAmount(s.percent))))
const settingsValid = computed(
  () =>
    shares.value.every((s) => {
      const p = parseAmount(s.percent)
      return p !== null && p >= 0 && p <= 100
    }) &&
    (mode.value !== 'FixedShares' || total.value === 100),
)

async function load() {
  loading.value = true
  error.value = null
  try {
    let key = periodKey.value
    if (!key) key = (await unwrap(api.GET('/api/periods/current'))).key
    split.value = await unwrap(api.GET('/api/split/{periodKey}', { params: { path: { periodKey: key } } }))
  } catch (e) {
    split.value = null
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

function goTo(key: string | null) {
  router.push(key ? { name: 'split', params: { period: key } } : { name: 'split' })
}

async function openSettings() {
  settingsError.value = null
  try {
    settings.value = await unwrap(api.GET('/api/split/settings'))
    mode.value = settings.value.mode
    shares.value = settings.value.shares.map((s) => ({ ...s, percent: String(s.percent).replace('.', ',') }))
    settingsOpen.value = true
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

async function saveSettings() {
  if (!settings.value || !settingsValid.value) return
  savingSettings.value = true
  settingsError.value = null
  try {
    await unwrap(
      api.PUT('/api/split/settings', {
        headers: ifMatch(settings.value.version),
        body: {
          mode: mode.value,
          shares: shares.value.map((s) => ({ userId: s.userId, percent: parseAmount(s.percent) ?? 0 })),
        },
      }),
    )
    settingsOpen.value = false
    await load()
  } catch (e) {
    settingsError.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    savingSettings.value = false
  }
}

watch(periodKey, load, { immediate: true })
</script>

<template>
  <div class="d-flex align-center flex-wrap mb-4 ga-2">
    <h1 class="text-h5 font-weight-bold mr-auto">Répartition des dépenses</h1>
    <PeriodNavigator :period="split?.period ?? null" @go="goTo" />
    <v-btn
      v-if="auth.isAdmin"
      :icon="mdiCogOutline"
      variant="text"
      aria-label="Mode de répartition"
      @click="openSettings"
    />
  </div>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
  <v-progress-linear v-if="loading" indeterminate color="primary" class="mb-2" />

  <template v-if="split">
    <v-card class="mb-4" variant="tonal" color="primary">
      <v-card-text>
        <div class="d-flex flex-wrap ga-4">
          <div>
            <div class="text-caption">Dépenses communes</div>
            <div class="text-h6 font-weight-bold" data-testid="split-common">
              {{ formatAmount(split.commonExpenses) }}
            </div>
          </div>
          <div>
            <div class="text-caption">dont comptes joints</div>
            <div class="text-h6">{{ formatAmount(split.jointExpenses) }}</div>
          </div>
          <div>
            <div class="text-caption">Total apporté</div>
            <div class="text-h6">{{ formatAmount(split.totalPaid) }}</div>
          </div>
        </div>
        <div class="text-body-2 mt-2">
          Répartition {{ splitModeTitle(split.appliedMode).toLowerCase() }}
          <template v-if="split.appliedMode !== split.mode"
            >(aucun revenu sur la période : parts égales)</template
          >
        </div>
      </v-card-text>
    </v-card>

    <v-row class="mb-2">
      <v-col v-for="m in split.members" :key="m.userId" cols="12" md="6">
        <v-card class="h-100" :data-testid="`split-member-${m.name}`">
          <v-card-title class="d-flex">
            <span class="mr-auto">{{ m.name }}</span>
            <v-chip size="small" variant="tonal">{{ m.share.toLocaleString('fr-FR') }} %</v-chip>
          </v-card-title>
          <v-card-subtitle :class="`text-${balanceColor(m.balance) ?? 'medium-emphasis'}`" class="text-wrap">
            {{ balanceText(m) }}
          </v-card-subtitle>
          <v-table density="compact">
            <tbody>
              <tr>
                <td>Dépenses payées</td>
                <td class="text-right">{{ formatAmount(m.expensesPaid) }}</td>
              </tr>
              <tr>
                <td>Versements aux comptes joints</td>
                <td class="text-right">{{ formatAmount(m.contributions) }}</td>
              </tr>
              <tr class="font-weight-medium">
                <td>Total apporté</td>
                <td class="text-right">{{ formatAmount(m.paid) }}</td>
              </tr>
              <tr>
                <td>Sa part</td>
                <td class="text-right">{{ formatAmount(m.due) }}</td>
              </tr>
              <tr class="font-weight-bold">
                <td>Solde</td>
                <td
                  class="text-right"
                  :class="`text-${balanceColor(m.balance) ?? ''}`"
                  data-testid="split-balance"
                >
                  {{ formatSignedAmount(m.balance) }}
                </td>
              </tr>
              <tr v-if="split.appliedMode === 'IncomeProRata'">
                <td class="text-medium-emphasis">Revenus de la période</td>
                <td class="text-right text-medium-emphasis">{{ formatAmount(m.income) }}</td>
              </tr>
            </tbody>
          </v-table>
        </v-card>
      </v-col>
    </v-row>

    <v-card class="mb-4" data-testid="settlements">
      <v-card-title>Pour équilibrer</v-card-title>
      <v-list v-if="split.settlements.length" density="compact">
        <v-list-item v-for="(s, i) in split.settlements" :key="i">
          <v-list-item-title>
            {{ s.fromName }} <v-icon :icon="mdiArrowRight" size="small" /> {{ s.toName }}
          </v-list-item-title>
          <template #append>
            <strong>{{ formatAmount(s.amount) }}</strong>
          </template>
        </v-list-item>
      </v-list>
      <v-card-text v-else class="text-medium-emphasis"
        >Rien à rembourser : chacun a apporté sa part.</v-card-text
      >
    </v-card>

    <v-alert type="info" variant="tonal" density="compact" class="text-body-2">
      Seuls les comptes partagés comptent. Une dépense payée depuis un compte qui a un titulaire est apportée
      par lui ; un virement vers un compte joint est un apport de celui à qui appartient le compte d'origine.
      Le titulaire se choisit dans la fiche du compte ; une catégorie peut être exclue de la répartition
      (dépense personnelle).
    </v-alert>
  </template>

  <v-dialog v-model="settingsOpen" max-width="480">
    <v-card title="Mode de répartition">
      <v-card-text>
        <v-alert v-if="settingsError" type="error" variant="tonal" class="mb-4" :text="settingsError" />
        <v-radio-group v-model="mode">
          <v-radio v-for="m in splitModes" :key="m.value" :value="m.value">
            <template #label>
              <div>
                <div>{{ m.title }}</div>
                <div class="text-caption text-medium-emphasis">{{ m.hint }}</div>
              </div>
            </template>
          </v-radio>
        </v-radio-group>
        <template v-if="mode === 'FixedShares'">
          <v-text-field
            v-for="s in shares"
            :key="s.userId"
            v-model="s.percent"
            :label="s.name"
            suffix="%"
            inputmode="decimal"
            density="compact"
          />
          <div :class="total === 100 ? 'text-success' : 'text-error'" class="text-body-2">
            Total : {{ total.toLocaleString('fr-FR') }} % (100 % attendus)
          </div>
        </template>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="settingsOpen = false">Annuler</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="savingSettings"
          :disabled="!settingsValid"
          @click="saveSettings"
        >
          Enregistrer
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
