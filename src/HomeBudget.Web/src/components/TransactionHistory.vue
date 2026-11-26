<script setup lang="ts">
import { ref, watch } from 'vue'
import { mdiHistory } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { describeChange } from '@/utils/transactionHistory'
import { formatDateTime } from '@/utils/users'

/** Historique des modifications d'une opération (B13), chargé à la demande. */
const props = defineProps<{ transactionId: string }>()

const open = ref(false)
const entries = ref<Schemas['TransactionChangeDto'][] | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

async function load() {
  loading.value = true
  error.value = null
  try {
    entries.value = await unwrap(
      api.GET('/api/transactions/{id}/history', { params: { path: { id: props.transactionId } } }),
    )
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

function toggle() {
  open.value = !open.value
  if (open.value && entries.value === null) void load()
}

watch(
  () => props.transactionId,
  () => {
    open.value = false
    entries.value = null
  },
)
</script>

<template>
  <div class="mt-2" data-testid="history">
    <v-btn :prepend-icon="mdiHistory" variant="text" size="small" class="px-1" @click="toggle">
      {{ open ? 'Masquer l’historique' : 'Historique des modifications' }}
    </v-btn>
    <template v-if="open">
      <v-progress-linear v-if="loading" indeterminate color="primary" class="my-1" />
      <v-alert v-if="error" type="error" variant="tonal" density="compact" :text="error" />
      <p v-else-if="entries && entries.length === 0" class="text-caption text-medium-emphasis">
        Aucune modification depuis la création.
      </p>
      <v-timeline v-else-if="entries" density="compact" side="end" truncate-line="both" class="mt-1">
        <v-timeline-item v-for="entry in entries" :key="entry.id" size="x-small" dot-color="primary">
          <div class="text-caption text-medium-emphasis">
            {{ formatDateTime(entry.at) }} · {{ entry.userName ?? 'Automatique' }}
          </div>
          <div v-for="(change, i) in entry.changes" :key="i" class="text-body-2">
            {{ describeChange(change) }}
          </div>
        </v-timeline-item>
      </v-timeline>
    </template>
  </div>
</template>
