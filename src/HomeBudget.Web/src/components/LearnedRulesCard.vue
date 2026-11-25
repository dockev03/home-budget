<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { mdiDeleteOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore } from '@/stores/reference'

/** Règles apprises des corrections (I2, I3) : consultation et suppression d'une règle erronée. */
const auth = useAuthStore()
const reference = useReferenceStore()
const rules = ref<Schemas['RuleDto'][]>([])
const error = ref<string | null>(null)
const deleting = ref<string | null>(null)

async function load() {
  try {
    rules.value = await unwrap(api.GET('/api/rules'))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

function describe(rule: Schemas['RuleDto']): string {
  const parts = [rule.payeeName, rule.categoryId ? reference.categoryLabel(rule.categoryId) : null]
  return parts.filter(Boolean).join(' · ') || 'Règle vide'
}

async function remove(rule: Schemas['RuleDto']) {
  deleting.value = rule.id
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/rules/{id}', { params: { path: { id: rule.id } }, headers: ifMatch(rule.version) }),
    )
    rules.value = rules.value.filter((r) => r.id !== rule.id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = null
  }
}

onMounted(load)
</script>

<template>
  <v-card class="mt-6" title="Règles apprises" data-testid="rules">
    <v-card-text>
      <p class="text-body-2 text-medium-emphasis mb-2">
        Quand vous corrigez le tiers ou la catégorie d’une opération importée, la règle est retenue et
        appliquée aux relevés suivants. Supprimer une règle ne modifie aucune opération.
      </p>
      <v-alert v-if="error" type="error" variant="tonal" class="mb-2" :text="error" />
      <v-list v-if="rules.length" density="compact">
        <v-list-item v-for="rule in rules" :key="rule.id" :title="rule.key" :subtitle="describe(rule)">
          <template #append>
            <v-btn
              v-if="auth.canWrite"
              :icon="mdiDeleteOutline"
              variant="text"
              size="small"
              :loading="deleting === rule.id"
              :aria-label="`Supprimer la règle ${rule.key}`"
              @click="remove(rule)"
            />
          </template>
        </v-list-item>
      </v-list>
      <p v-else class="text-body-2">Aucune règle pour l’instant.</p>
    </v-card-text>
  </v-card>
</template>
