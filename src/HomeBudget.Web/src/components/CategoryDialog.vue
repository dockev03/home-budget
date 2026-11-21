<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useReferenceStore, type Category } from '@/stores/reference'
import { categoryKinds, type CategoryKind } from '@/utils/labels'
import { required } from '@/utils/rules'
import AppearancePicker from './AppearancePicker.vue'

const props = defineProps<{
  category: Category | null
  defaultKind?: CategoryKind
  defaultParentId?: string | null
}>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [] }>()

const reference = useReferenceStore()
const form = ref({
  name: '',
  kind: 'Variable' as CategoryKind,
  parentId: null as string | null,
  icon: null as string | null,
  color: null as string | null,
  isArchived: false,
})
const valid = ref(false)
const saving = ref(false)
const deleting = ref(false)
const error = ref<string | null>(null)

const hasChildren = computed(
  () => !!props.category && reference.categories.some((c) => c.parentId === props.category!.id),
)
// Parents possibles : catégories principales actives, autres que celle modifiée.
const parents = computed(() =>
  reference.categories
    .filter((c) => !c.parentId && !c.isArchived && c.id !== props.category?.id)
    .map((c) => ({ value: c.id, title: c.name, kind: c.kind })),
)

watch(visible, (open) => {
  if (!open) return
  error.value = null
  const c = props.category
  form.value = c
    ? {
        name: c.name,
        kind: c.kind,
        parentId: c.parentId,
        icon: c.icon,
        color: c.color,
        isArchived: c.isArchived,
      }
    : {
        name: '',
        kind: props.defaultKind ?? 'Variable',
        parentId: props.defaultParentId ?? null,
        icon: null,
        color: null,
        isArchived: false,
      }
})

// Une sous-catégorie prend le type de son parent.
watch(
  () => form.value.parentId,
  (parentId) => {
    const parent = parentId ? reference.categoryById.get(parentId) : undefined
    if (parent) form.value.kind = parent.kind
  },
  { immediate: true },
)

async function save() {
  if (!valid.value) return
  saving.value = true
  error.value = null
  const body = { ...form.value, sortOrder: null }
  try {
    if (props.category) {
      await unwrap(
        api.PUT('/api/categories/{id}', {
          params: { path: { id: props.category.id } },
          headers: ifMatch(props.category.version),
          body,
        }),
      )
    } else {
      await unwrap(api.POST('/api/categories', { body }))
    }
    emit('saved')
    visible.value = false
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove() {
  if (!props.category) return
  deleting.value = true
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/categories/{id}', {
        params: { path: { id: props.category.id } },
        headers: ifMatch(props.category.version),
      }),
    )
    emit('saved')
    visible.value = false
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}
</script>

<template>
  <v-dialog v-model="visible" max-width="520" scrollable>
    <v-card :title="category ? 'Modifier la catégorie' : 'Nouvelle catégorie'">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <v-form v-model="valid" @submit.prevent="save">
          <v-text-field v-model="form.name" label="Nom" :rules="[required]" maxlength="60" autofocus />
          <v-select
            v-model="form.parentId"
            :items="parents"
            label="Catégorie parente"
            clearable
            :disabled="hasChildren"
            :hint="hasChildren ? 'Elle a des sous-catégories : elle reste une catégorie principale.' : ''"
            persistent-hint
            class="mb-2"
          />
          <v-select v-model="form.kind" :items="categoryKinds" label="Type" :disabled="!!form.parentId" />
          <AppearancePicker v-model:icon="form.icon" v-model:color="form.color" />
          <v-switch
            v-if="category"
            v-model="form.isArchived"
            label="Archivée (masquée à la saisie)"
            color="primary"
            hide-details
          />
          <button type="submit" hidden />
        </v-form>
      </v-card-text>
      <v-card-actions>
        <v-btn v-if="category" color="error" :loading="deleting" @click="remove">Supprimer</v-btn>
        <v-spacer />
        <v-btn @click="visible = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save">
          Enregistrer
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
