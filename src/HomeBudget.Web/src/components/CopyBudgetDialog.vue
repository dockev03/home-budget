<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { periodLabel } from '@/utils/budget'
import { describeDefinition } from '@/utils/budgetCopy'

/** Copie du budget (D5) vers la période affichée, avec aperçu et choix des catégories. */
const props = defineProps<{ periodKey: string | null }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [count: number] }>()

type Source = Schemas['BudgetCopySource']
type Proposal = Schemas['BudgetCopyProposalDto']

const source = ref<Source>('PreviousPeriod')
const months = ref(3)
const proposals = ref<Proposal[]>([])
const selected = ref<string[]>([])
const loading = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)

const monthOptions = [1, 2, 3, 6, 12].map((n) => ({ value: n, title: `${n} mois` }))
const title = computed(() => (props.periodKey ? `Copier le budget vers ${periodLabel(props.periodKey)}` : ''))

async function preview() {
  if (!props.periodKey) return
  loading.value = true
  error.value = null
  try {
    proposals.value = await unwrap(
      api.POST('/api/budgets/{periodKey}/copy/preview', {
        params: { path: { periodKey: props.periodKey } },
        body: { source: source.value, months: months.value, categoryIds: null },
      }),
    )
    // Les budgets automatiques (échéances) ne sont pas remplacés par défaut.
    selected.value = proposals.value.filter((p) => p.currentMode !== 'Auto').map((p) => p.categoryId)
  } catch (e) {
    proposals.value = []
    error.value = e instanceof ApiError ? e.message : 'Aperçu impossible.'
  } finally {
    loading.value = false
  }
}

async function apply() {
  if (!props.periodKey || selected.value.length === 0) return
  saving.value = true
  error.value = null
  try {
    const result = await unwrap(
      api.POST('/api/budgets/{periodKey}/copy', {
        params: { path: { periodKey: props.periodKey } },
        body: { source: source.value, months: months.value, categoryIds: selected.value },
      }),
    )
    emit('saved', result.applied)
    visible.value = false
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Copie impossible.'
  } finally {
    saving.value = false
  }
}

watch(visible, (open) => {
  if (open) preview()
})
watch([source, months], () => {
  if (visible.value) preview()
})
</script>

<template>
  <v-dialog v-model="visible" max-width="600" scrollable>
    <v-card :title="title">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <v-radio-group v-model="source" hide-details class="mb-2">
          <v-radio value="PreviousPeriod" label="Budget de la période précédente" />
          <v-radio value="Average" label="Moyenne du réalisé des derniers mois" />
        </v-radio-group>
        <v-select
          v-if="source === 'Average'"
          v-model="months"
          :items="monthOptions"
          label="Moyenne sur"
          density="compact"
          class="mb-2"
          hide-details
        />
        <v-progress-linear v-if="loading" indeterminate color="primary" class="my-2" />
        <p v-if="!loading && proposals.length === 0" class="text-body-2 text-medium-emphasis mt-4">
          Rien à copier : le budget de cette période est déjà identique.
        </p>
        <v-list v-else density="compact" class="mt-2">
          <v-list-item v-for="p in proposals" :key="p.categoryId" :class="{ 'pl-8': p.parentId }">
            <template #prepend>
              <v-checkbox-btn v-model="selected" :value="p.categoryId" :aria-label="`Copier ${p.name}`" />
            </template>
            <v-list-item-title>{{ p.name }}</v-list-item-title>
            <v-list-item-subtitle>
              {{ describeDefinition(p.currentMode, p.currentValue, p.currentBudgeted) }} →
              <strong>{{ describeDefinition(p.proposedMode, p.proposedValue, null) }}</strong>
            </v-list-item-subtitle>
          </v-list-item>
        </v-list>
        <p class="text-caption text-medium-emphasis mt-2">
          Les montants copiés deviennent le budget de cette seule période ; le budget par défaut ne change
          pas.
        </p>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="visible = false">Annuler</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="saving"
          :disabled="selected.length === 0 || loading"
          @click="apply"
        >
          Copier {{ selected.length }} budget{{ selected.length > 1 ? 's' : '' }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
