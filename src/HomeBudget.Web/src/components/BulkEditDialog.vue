<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { useReferenceStore } from '@/stores/reference'
import { bulkMessage, cleanTags } from '@/utils/learning'

/** Modification en masse (B10) des opérations cochées : seuls les champs renseignés changent. */
const props = defineProps<{ ids: string[] }>()
const open = defineModel<boolean>({ required: true })
const emit = defineEmits<{ done: [message: string] }>()

const reference = useReferenceStore()

const categoryId = ref<string | null>(null)
const payeeName = ref<string | null>(null)
const accountId = ref<string | null>(null)
const cleared = ref<boolean | null>(null)
const addTags = ref<string[]>([])
const removeTags = ref<string[]>([])
const tagItems = ref<string[]>([])
const payeeItems = ref<string[]>([])
const confirming = ref(false)
const saving = ref(false)
const error = ref<string | null>(null)

const categoryItems = computed(() =>
  reference.categories
    .filter((c) => !c.isArchived)
    .map((c) => ({ value: c.id, title: reference.categoryLabel(c.id) })),
)
const accountItems = computed(() => reference.activeAccounts.map((a) => ({ value: a.id, title: a.name })))
const clearedItems = [
  { value: null, title: 'Inchangé' },
  { value: true, title: 'Pointer' },
  { value: false, title: 'Dépointer' },
]

/** Résumé des modifications demandées, relu avant confirmation. */
const changes = computed(() => {
  const list: string[] = []
  if (categoryId.value) list.push(`catégorie « ${reference.categoryLabel(categoryId.value)} »`)
  if (payeeName.value?.trim()) list.push(`tiers « ${payeeName.value.trim()} »`)
  if (accountId.value) list.push(`compte « ${reference.accountById.get(accountId.value)?.name ?? '?'} »`)
  if (cleared.value !== null) list.push(cleared.value ? 'pointées' : 'dépointées')
  const added = cleanTags(addTags.value)
  if (added.length) list.push(`tags ajoutés : ${added.join(', ')}`)
  const removed = cleanTags(removeTags.value)
  if (removed.length) list.push(`tags retirés : ${removed.join(', ')}`)
  return list
})

watch(open, async (value) => {
  if (!value) return
  categoryId.value = null
  payeeName.value = null
  accountId.value = null
  cleared.value = null
  addTags.value = []
  removeTags.value = []
  confirming.value = false
  error.value = null
  const { data } = await api.GET('/api/tags')
  tagItems.value = (data ?? []).map((t) => t.name)
})

// Toute modification du formulaire demande une nouvelle confirmation.
watch(changes, () => (confirming.value = false))

let searchTimer: ReturnType<typeof setTimeout> | undefined
function searchPayees(text: string | null) {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(async () => {
    const { data } = await api.GET('/api/payees', {
      params: { query: { search: text ?? undefined, limit: 10 } },
    })
    payeeItems.value = (data ?? []).map((p) => p.name)
  }, 200)
}

async function apply() {
  if (!changes.value.length) return
  if (!confirming.value) {
    confirming.value = true
    return
  }
  saving.value = true
  error.value = null
  try {
    const added = cleanTags(addTags.value)
    const removed = cleanTags(removeTags.value)
    const result = await unwrap(
      api.POST('/api/transactions/bulk', {
        body: {
          ids: props.ids,
          category: categoryId.value ? { categoryId: categoryId.value } : null,
          payeeName: payeeName.value?.trim() || null,
          accountId: accountId.value,
          isCleared: cleared.value,
          addTags: added.length ? added : null,
          removeTags: removed.length ? removed : null,
        },
      }),
    )
    if (accountId.value) await reference.loadAccounts()
    open.value = false
    emit('done', bulkMessage(result))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Modification impossible.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <v-dialog v-model="open" max-width="560" scrollable>
    <v-card :title="`Modifier ${ids.length} opération(s)`">
      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <p class="text-body-2 text-medium-emphasis mb-4">
          Seuls les champs renseignés sont modifiés. Les virements, les opérations ventilées (pour la
          catégorie) et les opérations importées (pour le compte) sont laissés intacts.
        </p>
        <v-autocomplete v-model="categoryId" :items="categoryItems" label="Catégorie" clearable />
        <v-combobox
          v-model="payeeName"
          :items="payeeItems"
          label="Tiers"
          clearable
          @update:search="searchPayees"
        />
        <v-select v-model="accountId" :items="accountItems" label="Compte" clearable />
        <v-select v-model="cleared" :items="clearedItems" label="Pointage" />
        <v-combobox
          v-model="addTags"
          :items="tagItems"
          label="Ajouter des tags"
          multiple
          chips
          closable-chips
          :delimiters="[',']"
        />
        <v-combobox
          v-model="removeTags"
          :items="tagItems"
          label="Retirer des tags"
          multiple
          chips
          closable-chips
          :delimiters="[',']"
        />
        <v-alert v-if="confirming" type="warning" variant="tonal" data-testid="bulk-summary">
          {{ ids.length }} opération(s) : {{ changes.join(' ; ') }}. Confirmez pour appliquer.
        </v-alert>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="open = false">Annuler</v-btn>
        <v-btn
          :color="confirming ? 'warning' : 'primary'"
          variant="flat"
          :loading="saving"
          :disabled="!changes.length"
          @click="apply"
          >{{ confirming ? 'Confirmer' : 'Modifier' }}</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
