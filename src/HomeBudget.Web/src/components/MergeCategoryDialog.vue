<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useReferenceStore, type Category } from '@/stores/reference'

/** Fusion d'une catégorie dans une autre (C5) : aperçu de ce qui est déplacé, puis confirmation. */
const props = defineProps<{ category: Category | null }>()
const open = defineModel<boolean>({ required: true })
const emit = defineEmits<{ merged: [message: string] }>()

const reference = useReferenceStore()
const targetId = ref<string | null>(null)
const preview = ref<Schemas['MergeDto'] | null>(null)
const loading = ref(false)
const merging = ref(false)
const error = ref<string | null>(null)

/** Cibles possibles : même type, ni la catégorie elle-même, ni l'une de ses sous-catégories. */
const targets = computed(() => {
  const source = props.category
  if (!source) return []
  const hasChildren = reference.categories.some((c) => c.parentId === source.id)
  return reference.categories
    .filter(
      (c) =>
        c.id !== source.id &&
        c.kind === source.kind &&
        c.parentId !== source.id &&
        (!hasChildren || !c.parentId),
    )
    .map((c) => ({ value: c.id, title: reference.categoryLabel(c.id) }))
})

const summary = computed(() => {
  const p = preview.value
  if (!p) return []
  const items = [
    `${p.lines} ligne(s) d’opération`,
    `${p.recurrences} récurrence(s)`,
    `${p.rules} règle(s) apprise(s)`,
  ]
  if (p.subcategories) items.push(`${p.subcategories} sous-catégorie(s)`)
  if (p.budgetMoved) items.push('le budget')
  return items
})

watch(open, (value) => {
  if (!value) return
  targetId.value = null
  preview.value = null
  error.value = null
})

watch(targetId, async (id) => {
  preview.value = null
  error.value = null
  if (!id || !props.category) return
  loading.value = true
  try {
    preview.value = await unwrap(
      api.GET('/api/categories/{id}/merge-preview', {
        params: { path: { id: props.category.id }, query: { targetId: id } },
      }),
    )
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Aperçu impossible.'
  } finally {
    loading.value = false
  }
})

async function merge() {
  if (!props.category || !targetId.value || !preview.value) return
  merging.value = true
  error.value = null
  try {
    const result = await unwrap(
      api.POST('/api/categories/{id}/merge', {
        params: { path: { id: props.category.id } },
        headers: ifMatch(props.category.version),
        body: { targetId: targetId.value },
      }),
    )
    open.value = false
    emit('merged', `« ${result.sourceName} » fusionnée dans « ${result.targetName} ».`)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Fusion impossible.'
  } finally {
    merging.value = false
  }
}
</script>

<template>
  <v-dialog v-model="open" max-width="520">
    <v-card :title="`Fusionner « ${category?.name ?? ''} »`">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <p class="text-body-2 text-medium-emphasis mb-4">
          Tout ce qui utilise cette catégorie passe dans la catégorie choisie, puis elle est supprimée.
        </p>
        <v-autocomplete
          v-model="targetId"
          :items="targets"
          label="Fusionner dans"
          :loading="loading"
          no-data-text="Aucune catégorie du même type"
        />
        <v-alert v-if="preview" type="warning" variant="tonal" data-testid="merge-summary">
          Seront déplacés vers « {{ preview.targetName }} » : {{ summary.join(', ') }}.
          <template v-if="preview.budgetDropped">
            Le budget de « {{ preview.sourceName }} » est supprimé : celui de « {{ preview.targetName }} » est
            conservé.</template
          >
          « {{ preview.sourceName }} » sera ensuite supprimée.
        </v-alert>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="open = false">Annuler</v-btn>
        <v-btn color="error" variant="flat" :loading="merging" :disabled="!preview" @click="merge"
          >Fusionner et supprimer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
