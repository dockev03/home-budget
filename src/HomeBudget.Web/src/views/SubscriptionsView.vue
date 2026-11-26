<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { useReferenceStore } from '@/stores/reference'
import { formatAmount, formatDate } from '@/utils/format'
import { describeFrequency } from '@/utils/recurrence'

/** Vue « Abonnements » (E10) : coût mensuel et annuel des récurrences marquées comme abonnement. */
const reference = useReferenceStore()
const data = ref<Schemas['SubscriptionsDto'] | null>(null)
const error = ref<string | null>(null)

onMounted(async () => {
  try {
    await reference.load()
    data.value = await unwrap(api.GET('/api/recurrences/subscriptions'))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
})
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Abonnements</h1>
  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <template v-if="data">
    <v-row class="mb-2">
      <v-col cols="6">
        <v-card class="pa-4" data-testid="subscriptions-monthly">
          <div class="text-caption text-medium-emphasis">Par mois</div>
          <div class="text-h6">{{ formatAmount(data.monthlyTotal) }}</div>
        </v-card>
      </v-col>
      <v-col cols="6">
        <v-card class="pa-4" data-testid="subscriptions-annual">
          <div class="text-caption text-medium-emphasis">Par an</div>
          <div class="text-h6">{{ formatAmount(data.annualTotal) }}</div>
        </v-card>
      </v-col>
    </v-row>

    <v-card v-if="!data.items.length" class="pa-6 text-center text-medium-emphasis">
      Aucun abonnement. Cochez « Abonnement » dans une récurrence pour la compter ici.
    </v-card>
    <v-card v-else>
      <v-list lines="two">
        <v-list-item
          v-for="s in data.items"
          :key="s.recurrenceId"
          :title="s.payeeName ?? s.label"
          :subtitle="`${describeFrequency(s.unit, s.interval)} · ${formatAmount(Math.abs(s.amount))}${s.nextDate ? ` · prochaine le ${formatDate(s.nextDate)}` : ''} · ${reference.categoryLabel(s.categoryId)}`"
        >
          <template #append>
            <div class="text-right">
              <div class="font-weight-medium text-no-wrap">{{ formatAmount(s.monthlyCost) }} / mois</div>
              <div class="text-caption text-medium-emphasis text-no-wrap">
                {{ formatAmount(s.annualCost) }} / an
              </div>
            </div>
          </template>
        </v-list-item>
      </v-list>
    </v-card>
    <div class="mt-4">
      <v-btn variant="text" color="primary" :to="{ name: 'recurrences' }">Gérer les récurrences</v-btn>
    </div>
  </template>
</template>
