<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiAlertOutline, mdiCalendarCheckOutline, mdiShieldAlertOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { usePendingStore } from '@/stores/pending'
import { useReferenceStore } from '@/stores/reference'
import { formatAmount, formatDate } from '@/utils/format'
import { iconPath } from '@/utils/icons'

const auth = useAuthStore()
const reference = useReferenceStore()
const pending = usePendingStore()
const forecast = ref<Schemas['ForecastDto'] | null>(null)
const error = ref<string | null>(null)

const rav = computed(() => forecast.value?.remainingToLive)
const hasForecastAccounts = computed(() => (forecast.value?.accountIds.length ?? 0) > 0)

onMounted(async () => {
  try {
    const [, , result] = await Promise.all([
      reference.load(true),
      pending.load(),
      unwrap(api.GET('/api/forecast')),
    ])
    forecast.value = result
  } catch {
    error.value = 'Chargement impossible.'
  }
})
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Bonjour {{ auth.user?.userName }}</h1>

  <v-alert
    v-if="auth.user && !auth.user.twoFactorEnabled"
    type="warning"
    variant="tonal"
    :icon="mdiShieldAlertOutline"
    class="mb-4"
    title="Double authentification désactivée"
  >
    Protégez l'accès à vos comptes en activant la double authentification.
    <template #append>
      <v-btn :to="{ name: 'security' }" variant="flat" color="warning">Activer</v-btn>
    </template>
  </v-alert>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <v-alert
    v-if="pending.count > 0"
    type="info"
    variant="tonal"
    :icon="mdiCalendarCheckOutline"
    class="mb-4"
    :title="pending.count > 1 ? `${pending.count} échéances à valider` : '1 échéance à valider'"
  >
    <template #append>
      <v-btn :to="{ name: 'pending' }" variant="flat" color="info">Voir</v-btn>
    </template>
  </v-alert>

  <v-card v-if="forecast" class="mb-4" data-testid="forecast">
    <v-card-title class="d-flex align-center flex-wrap">
      Fin de période
      <v-spacer />
      <span class="text-body-2 text-medium-emphasis"
        >{{ formatDate(forecast.period.start) }} – {{ formatDate(forecast.period.end) }}</span
      >
    </v-card-title>

    <v-card-text v-if="!hasForecastAccounts" class="text-medium-emphasis">
      Aucun compte n’est inclus dans la prévision : activez l’option sur vos comptes courants.
    </v-card-text>

    <template v-else>
      <v-alert
        v-if="forecast.thresholdDate"
        type="warning"
        variant="tonal"
        density="compact"
        :icon="mdiAlertOutline"
        class="mx-4 mb-2"
      >
        Passage sous le seuil de {{ formatAmount(forecast.threshold) }} prévu le
        <strong>{{ formatDate(forecast.thresholdDate) }}</strong>.
      </v-alert>

      <v-row class="px-4 pb-2" dense>
        <v-col cols="6" md="3">
          <div class="text-caption text-medium-emphasis">Solde actuel</div>
          <div class="text-h6" :class="forecast.currentBalance < 0 ? 'text-error' : ''">
            {{ formatAmount(forecast.currentBalance) }}
          </div>
        </v-col>
        <v-col cols="6" md="3">
          <div class="text-caption text-medium-emphasis">Solde prévu</div>
          <div class="text-h6" :class="forecast.endBalance < 0 ? 'text-error' : ''" data-testid="end-balance">
            {{ formatAmount(forecast.endBalance) }}
          </div>
        </v-col>
        <v-col cols="6" md="3">
          <div class="text-caption text-medium-emphasis">Reste à vivre</div>
          <div class="text-h6" :class="(rav?.total ?? 0) < 0 ? 'text-error' : 'text-success'">
            {{ formatAmount(rav?.total ?? 0) }}
          </div>
        </v-col>
        <v-col cols="6" md="3">
          <div class="text-caption text-medium-emphasis">
            Par jour ({{ rav?.daysLeft }} {{ rav?.daysLeft === 1 ? 'jour' : 'jours' }})
          </div>
          <div class="text-h6">{{ formatAmount(rav?.perDay ?? 0) }}</div>
        </v-col>
      </v-row>

      <v-expansion-panels variant="accordion" flat class="px-2 pb-2">
        <v-expansion-panel title="Détail du calcul">
          <v-expansion-panel-text>
            <v-table density="compact">
              <tbody>
                <tr>
                  <td>Solde actuel des comptes inclus</td>
                  <td class="text-right">{{ formatAmount(forecast.currentBalance) }}</td>
                </tr>
                <tr>
                  <td>Opérations déjà saisies à venir</td>
                  <td class="text-right">{{ formatAmount(forecast.futureTransactions) }}</td>
                </tr>
                <tr>
                  <td>Échéances de récurrence restantes</td>
                  <td class="text-right">{{ formatAmount(forecast.scheduled) }}</td>
                </tr>
                <tr>
                  <td>
                    Budget restant non couvert par une échéance
                    <span v-if="!forecast.includesIncome" class="text-caption">(revenus exclus)</span>
                  </td>
                  <td class="text-right">{{ formatAmount(forecast.budget) }}</td>
                </tr>
                <tr class="font-weight-bold">
                  <td>Solde prévu en fin de période</td>
                  <td class="text-right">{{ formatAmount(forecast.endBalance) }}</td>
                </tr>
              </tbody>
            </v-table>
            <p class="text-caption text-medium-emphasis mt-2">
              Reste à vivre = solde actuel + revenus attendus ({{ formatAmount(rav?.income ?? 0) }}) −
              dépenses fixes restantes ({{ formatAmount(rav?.fixed ?? 0) }}) − épargne prévue ({{
                formatAmount(rav?.savings ?? 0)
              }}) − seuil de sécurité ({{ formatAmount(rav?.safetyThreshold ?? 0) }}).
            </p>
          </v-expansion-panel-text>
        </v-expansion-panel>
      </v-expansion-panels>
    </template>
  </v-card>

  <v-card class="mb-4">
    <v-card-title class="d-flex align-center">
      Comptes
      <v-spacer />
      <span class="text-body-1"
        >Patrimoine net : <strong>{{ formatAmount(reference.netWorth) }}</strong></span
      >
    </v-card-title>
    <v-list v-if="reference.activeAccounts.length">
      <v-list-item
        v-for="account in reference.activeAccounts"
        :key="account.id"
        :title="account.name"
        :to="{ name: 'transactions', query: { accountId: account.id } }"
      >
        <template #prepend>
          <v-avatar :color="account.color ?? 'primary'" variant="tonal" size="36">
            <v-icon :icon="iconPath(account.icon)" size="20" />
          </v-avatar>
        </template>
        <template #append>
          <span class="font-weight-medium" :class="account.balance < 0 ? 'text-error' : ''">
            {{ formatAmount(account.balance) }}
          </span>
        </template>
      </v-list-item>
    </v-list>
    <v-card-text v-else class="text-medium-emphasis">
      Aucun compte pour l'instant.
      <v-btn :to="{ name: 'accounts' }" variant="text" color="primary">Créer un compte</v-btn>
    </v-card-text>
  </v-card>
</template>
