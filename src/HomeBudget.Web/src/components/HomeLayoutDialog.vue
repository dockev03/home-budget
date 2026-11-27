<script setup lang="ts">
import { ref, watch } from 'vue'
import { mdiArrowDown, mdiArrowUp, mdiEyeOffOutline, mdiEyeOutline } from '@mdi/js'
import { api } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { blockTitle, moveBlock, normalizeLayout, toggleHidden, type HomeLayout } from '@/utils/homeLayout'

/** Personnaliser l'accueil : ordre des blocs et blocs masqués, enregistrés pour l'utilisateur. */
const props = defineProps<{ layout: HomeLayout }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [layout: HomeLayout] }>()

const draft = ref<HomeLayout>({ order: [], hidden: [] })
const saving = ref(false)
const error = ref<string | null>(null)

watch(visible, (open) => {
  if (!open) return
  draft.value = { order: [...props.layout.order], hidden: [...props.layout.hidden] }
  error.value = null
})

async function run(request: () => Promise<{ order: string[]; hidden: string[] }>) {
  saving.value = true
  error.value = null
  try {
    const result = await request()
    emit('saved', normalizeLayout(result.order, result.hidden))
    visible.value = false
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

const save = () =>
  run(() =>
    unwrap(
      api.PUT('/api/dashboard/layout', { body: { order: draft.value.order, hidden: draft.value.hidden } }),
    ),
  )
const reset = () => run(() => unwrap(api.DELETE('/api/dashboard/layout')))
</script>

<template>
  <v-dialog v-model="visible" max-width="520" scrollable>
    <v-card title="Personnaliser l’accueil" data-testid="home-layout-dialog">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <p class="text-body-2 text-medium-emphasis mb-2">
          Choisissez l’ordre des blocs et ceux à masquer. La disposition vous suit sur tous vos appareils.
        </p>
        <v-list density="compact">
          <v-list-item
            v-for="(id, index) in draft.order"
            :key="id"
            :class="draft.hidden.includes(id) ? 'text-disabled' : ''"
            :data-testid="`layout-${id}`"
          >
            <v-list-item-title class="text-wrap">{{ blockTitle(id) }}</v-list-item-title>
            <template #append>
              <v-btn
                :icon="mdiArrowUp"
                variant="text"
                size="small"
                :disabled="index === 0"
                :aria-label="`Monter « ${blockTitle(id)} »`"
                data-testid="layout-up"
                @click="draft.order = moveBlock(draft.order, id, -1)"
              />
              <v-btn
                :icon="mdiArrowDown"
                variant="text"
                size="small"
                :disabled="index === draft.order.length - 1"
                :aria-label="`Descendre « ${blockTitle(id)} »`"
                data-testid="layout-down"
                @click="draft.order = moveBlock(draft.order, id, 1)"
              />
              <v-btn
                :icon="draft.hidden.includes(id) ? mdiEyeOffOutline : mdiEyeOutline"
                variant="text"
                size="small"
                :aria-label="`${draft.hidden.includes(id) ? 'Afficher' : 'Masquer'} « ${blockTitle(id)} »`"
                :aria-pressed="draft.hidden.includes(id)"
                data-testid="layout-toggle"
                @click="draft = toggleHidden(draft, id)"
              />
            </template>
          </v-list-item>
        </v-list>
      </v-card-text>
      <v-card-actions class="flex-wrap">
        <v-btn :disabled="saving" data-testid="layout-reset" @click="reset">Rétablir par défaut</v-btn>
        <v-spacer />
        <v-btn @click="visible = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" data-testid="layout-save" @click="save"
          >Enregistrer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
