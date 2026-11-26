<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { useReferenceStore } from '@/stores/reference'
import { auditActions, auditSeverity, auditTargetLabel, formatDateTime } from '@/utils/users'

type Entry = Schemas['AuditEntryDto']
type Action = Schemas['AuditAction']

/** Journal d'audit (N6) : connexions, échecs, droits, exports, restaurations et suppressions. */
const reference = useReferenceStore()

const pageSize = 50
const entries = ref<Entry[]>([])
const total = ref(0)
const page = ref(1)
const action = ref<Action | null>(null)
const userId = ref<string | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)

const pages = computed(() => Math.max(1, Math.ceil(total.value / pageSize)))
const actionItems = Object.entries(auditActions).map(([value, title]) => ({ value, title }))
const userItems = computed(() => [...reference.userNames].map(([value, title]) => ({ value, title })))

async function load() {
  loading.value = true
  error.value = null
  try {
    const result = await unwrap(
      api.GET('/api/audit', {
        params: {
          query: {
            page: page.value,
            pageSize,
            action: action.value ?? undefined,
            userId: userId.value ?? undefined,
          },
        },
      }),
    )
    entries.value = result.items
    total.value = result.total
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

watch([action, userId], () => {
  if (page.value === 1) void load()
  else page.value = 1
})
watch(page, load)

onMounted(async () => {
  await Promise.all([load(), reference.loadUsers()])
})
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Journal d’audit</h1>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <v-row dense class="mb-2">
    <v-col cols="12" sm="6">
      <v-select v-model="action" :items="actionItems" label="Action" clearable hide-details />
    </v-col>
    <v-col cols="12" sm="6">
      <v-select v-model="userId" :items="userItems" label="Utilisateur" clearable hide-details />
    </v-col>
  </v-row>

  <v-card :loading="loading">
    <v-list v-if="entries.length" lines="two" density="compact" data-testid="audit-list">
      <v-list-item v-for="entry in entries" :key="entry.id">
        <v-list-item-title class="d-flex align-center ga-2 flex-wrap">
          <v-chip size="small" :color="auditSeverity(entry.action)" label>
            {{ auditActions[entry.action] ?? entry.action }}
          </v-chip>
          <span v-if="entry.target" class="text-medium-emphasis">{{ auditTargetLabel(entry.target) }}</span>
          <span>{{ entry.details }}</span>
        </v-list-item-title>
        <v-list-item-subtitle>
          {{ formatDateTime(entry.at) }} · {{ entry.userName ?? 'anonyme' }}
          <template v-if="entry.ipAddress"> · {{ entry.ipAddress }}</template>
        </v-list-item-subtitle>
      </v-list-item>
    </v-list>
    <v-card-text v-else-if="!loading" class="text-medium-emphasis">Aucune entrée.</v-card-text>
  </v-card>

  <v-pagination v-if="pages > 1" v-model="page" :length="pages" :total-visible="5" class="mt-4" />
</template>
