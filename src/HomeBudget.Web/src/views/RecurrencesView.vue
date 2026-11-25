<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiAlertCircleOutline, mdiPlus, mdiSwapHorizontal } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import RecurrenceDialog from '@/components/RecurrenceDialog.vue'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore } from '@/stores/reference'
import { formatDate, formatSignedAmount } from '@/utils/format'
import { describeEnd, describeFrequency } from '@/utils/recurrence'

type Recurrence = Schemas['RecurrenceDto']

const auth = useAuthStore()
const reference = useReferenceStore()
const recurrences = ref<Recurrence[]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const dialog = ref(false)
const editing = ref<Recurrence | null>(null)

/** En cours d'abord (par prochaine échéance), puis terminées. */
const sorted = computed(() =>
  [...recurrences.value].sort((a, b) => {
    if (!a.nextDate || !b.nextDate) return a.nextDate ? -1 : b.nextDate ? 1 : a.label.localeCompare(b.label)
    return a.nextDate.localeCompare(b.nextDate) || a.label.localeCompare(b.label)
  }),
)

function summary(r: Recurrence): string {
  const account = reference.accountById.get(r.accountId)?.name ?? 'Compte inconnu'
  const target = r.toAccountId
    ? ` → ${reference.accountById.get(r.toAccountId)?.name ?? 'Compte inconnu'}`
    : ''
  return [
    describeFrequency(r.unit, r.interval),
    describeEnd(r.end, r.endDate, r.count),
    `${account}${target}`,
    r.toAccountId ? null : reference.categoryLabel(r.categoryId),
  ]
    .filter(Boolean)
    .join(' · ')
}

/** Effet sur le compte source : un virement est une sortie. */
const displayedAmount = (r: Recurrence) => (r.toAccountId ? -r.amount : r.amount)

async function load() {
  loading.value = true
  error.value = null
  try {
    await reference.load()
    recurrences.value = await unwrap(api.GET('/api/recurrences'))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

function edit(recurrence: Recurrence | null) {
  editing.value = recurrence
  dialog.value = true
}

onMounted(load)
</script>

<template>
  <div class="d-flex align-center mb-4">
    <h1 class="text-h5 font-weight-bold">Récurrences</h1>
    <v-spacer />
    <v-btn v-if="auth.canWrite" color="primary" :prepend-icon="mdiPlus" @click="edit(null)"
      >Nouvelle récurrence</v-btn
    >
  </div>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <v-card :loading="loading">
    <v-list v-if="sorted.length" lines="two">
      <v-list-item
        v-for="r in sorted"
        :key="r.id"
        :title="r.label"
        :subtitle="summary(r)"
        :disabled="!auth.canWrite"
        @click="edit(r)"
      >
        <template #prepend>
          <v-avatar :color="r.toAccountId ? 'secondary' : 'primary'" variant="tonal" size="36">
            <v-icon v-if="r.toAccountId" :icon="mdiSwapHorizontal" size="20" />
            <span v-else class="text-caption font-weight-bold">{{ r.mode === 'Automatic' ? 'A' : 'V' }}</span>
          </v-avatar>
        </template>
        <template #append>
          <div class="text-right">
            <div class="font-weight-medium" :class="displayedAmount(r) < 0 ? '' : 'text-success'">
              {{ formatSignedAmount(displayedAmount(r)) }}
            </div>
            <div class="text-caption text-medium-emphasis">
              <template v-if="r.nextDate">{{ formatDate(r.nextDate) }}</template>
              <template v-else>Terminée</template>
              <v-chip
                v-if="r.pendingCount"
                :prepend-icon="mdiAlertCircleOutline"
                color="warning"
                size="x-small"
                class="ml-1"
                :to="{ name: 'pending' }"
                >{{ r.pendingCount }} à valider</v-chip
              >
            </div>
          </div>
        </template>
      </v-list-item>
    </v-list>
    <v-card-text v-else-if="!loading" class="text-medium-emphasis">
      Aucune récurrence : loyer, abonnements, salaire, épargne mensuelle… Saisissez-les une fois, elles
      alimentent le budget automatique et la prévision.
    </v-card-text>
  </v-card>

  <p class="text-caption text-medium-emphasis mt-2">
    A : opération créée automatiquement · V : échéance à valider.
  </p>

  <RecurrenceDialog v-model="dialog" :recurrence="editing" @saved="load" />
</template>
