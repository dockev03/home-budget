<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { mdiAlertCircleOutline, mdiCheckCircleOutline, mdiLockOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { formatDateTime } from '@/utils/users'
import { agentLabel } from '@/utils/sessions'

/** Historique des connexions de l'utilisateur (N6) : réussites, échecs et verrouillages, avec l'appareil et l'IP. */
const pageSize = 20
const entries = ref<Schemas['LoginEntryDto'][]>([])
const total = ref(0)
const page = ref(1)
const retentionDays = ref<number | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))

const outcomes: Record<string, { label: string; icon: string; color: string }> = {
  LoginSucceeded: { label: 'Connexion réussie', icon: mdiCheckCircleOutline, color: 'success' },
  LoginFailed: { label: 'Échec de connexion', icon: mdiAlertCircleOutline, color: 'error' },
  LoginLockedOut: { label: 'Compte verrouillé', icon: mdiLockOutline, color: 'error' },
}
const outcome = (action: string) => outcomes[action] ?? outcomes.LoginFailed!

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await unwrap(
      api.GET('/api/account/logins', { params: { query: { page: page.value, pageSize } } }),
    )
    entries.value = result.items
    total.value = result.total
    retentionDays.value = result.retentionDays ?? null
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

watch(page, load)
onMounted(load)
</script>

<template>
  <v-card :loading="loading" data-testid="login-history">
    <v-card-title>Historique des connexions</v-card-title>
    <v-card-subtitle class="text-wrap">
      Connexions et tentatives sur votre compte. Une tentative que vous ne reconnaissez pas : changez votre
      mot de passe et activez la double authentification.
      <template v-if="retentionDays"> Conservé {{ retentionDays }} jours.</template>
    </v-card-subtitle>
    <v-card-text v-if="error" class="pb-0">
      <v-alert type="error" variant="tonal" :text="error" />
    </v-card-text>
    <v-list v-if="entries.length" lines="two" density="compact">
      <v-list-item
        v-for="entry in entries"
        :key="entry.id"
        :prepend-icon="outcome(entry.action).icon"
        :base-color="outcome(entry.action).color === 'error' ? 'error' : undefined"
        data-testid="login-entry"
      >
        <v-list-item-title class="d-flex align-center flex-wrap ga-2">
          {{ formatDateTime(entry.at) }}
          <v-chip size="x-small" :color="outcome(entry.action).color" label>
            {{ outcome(entry.action).label }}
          </v-chip>
        </v-list-item-title>
        <v-list-item-subtitle class="text-wrap">
          {{ agentLabel(entry) ?? 'Navigateur non enregistré' }} · IP {{ entry.ipAddress ?? 'inconnue' }}
          <template v-if="entry.details"> · {{ entry.details }}</template>
        </v-list-item-subtitle>
      </v-list-item>
    </v-list>
    <v-card-text v-else-if="!loading" class="text-medium-emphasis">Aucune connexion enregistrée.</v-card-text>
    <v-card-actions v-if="pages > 1" class="justify-center">
      <v-pagination v-model="page" :length="pages" :total-visible="5" density="comfortable" />
    </v-card-actions>
  </v-card>
</template>
