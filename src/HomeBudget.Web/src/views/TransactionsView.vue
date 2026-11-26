<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  mdiCheckCircle,
  mdiCheckboxMarkedOutline,
  mdiCheckboxBlankCircleOutline,
  mdiDownload,
  mdiFileUploadOutline,
  mdiFilterVariant,
  mdiLockOutline,
  mdiMagnify,
  mdiPencilBoxMultipleOutline,
  mdiSwapHorizontal,
} from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import type { paths } from '@/api/schema'
import { ApiError, unwrap } from '@/api/errors'
import BulkEditDialog from '@/components/BulkEditDialog.vue'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore } from '@/stores/reference'
import { useTransactionEditor, type Transaction } from '@/stores/transactionEditor'
import { transactionsExportUrl, type TransactionFilters } from '@/utils/exports'
import { formatDayHeading, formatSignedAmount, parseAmount } from '@/utils/format'
import { netCostText } from '@/utils/reconciliation'
import { iconPath } from '@/utils/icons'

type Query = paths['/api/transactions']['get']['parameters']['query']

const route = useRoute()
const router = useRouter()
const reference = useReferenceStore()
const editor = useTransactionEditor()
const auth = useAuthStore()

const items = ref<Transaction[]>([])
const nextCursor = ref<string | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const filtersOpen = ref(false)
const tags = ref<Schemas['TagDto'][]>([])
// Sélection pour la modification en masse (B10).
const selecting = ref(false)
const selected = ref<string[]>([])
const bulkOpen = ref(false)
const success = ref<string | null>(null)

// Filtres, synchronisés avec l'URL (lien partageable, retour arrière).
const filters = ref<TransactionFilters>({
  accountId: null,
  categoryId: null,
  tagId: null,
  search: '',
  from: '',
  to: '',
  minAmount: '',
  maxAmount: '',
  cleared: null,
})

const accountItems = computed(() => reference.accounts.map((a) => ({ value: a.id, title: a.name })))
const categoryItems = computed(() =>
  reference.categories.map((c) => ({ value: c.id, title: reference.categoryLabel(c.id) })),
)
const tagItems = computed(() => tags.value.map((t) => ({ value: t.id, title: t.name })))
const clearedItems = [
  { value: null, title: 'Toutes' },
  { value: false, title: 'Non pointées' },
  { value: true, title: 'Pointées' },
]
const activeFilterCount = computed(
  () =>
    [
      filters.value.categoryId,
      filters.value.tagId,
      filters.value.from,
      filters.value.to,
      filters.value.minAmount,
      filters.value.maxAmount,
    ].filter(Boolean).length + (filters.value.cleared === null ? 0 : 1),
)

const groups = computed(() => {
  const byDate = new Map<string, Transaction[]>()
  for (const t of items.value) {
    byDate.set(t.date, [...(byDate.get(t.date) ?? []), t])
  }
  return [...byDate.entries()].map(([date, transactions]) => ({ date, transactions }))
})

function readRoute() {
  const q = route.query
  const str = (v: unknown) => (typeof v === 'string' ? v : '')
  filters.value = {
    accountId: str(q.accountId) || null,
    categoryId: str(q.categoryId) || null,
    tagId: str(q.tagId) || null,
    search: str(q.search),
    from: str(q.from),
    to: str(q.to),
    minAmount: str(q.minAmount),
    maxAmount: str(q.maxAmount),
    cleared: q.cleared === 'true' ? true : q.cleared === 'false' ? false : null,
  }
}

function writeRoute() {
  const f = filters.value
  const query: Record<string, string> = {}
  if (f.accountId) query.accountId = f.accountId
  if (f.categoryId) query.categoryId = f.categoryId
  if (f.tagId) query.tagId = f.tagId
  if (f.search) query.search = f.search
  if (f.from) query.from = f.from
  if (f.to) query.to = f.to
  if (f.minAmount) query.minAmount = f.minAmount
  if (f.maxAmount) query.maxAmount = f.maxAmount
  if (f.cleared !== null) query.cleared = String(f.cleared)
  void router.replace({ query })
}

function apiQuery(cursor: string | null): Query {
  const f = filters.value
  return {
    accountId: f.accountId ?? undefined,
    categoryId: f.categoryId ?? undefined,
    tagId: f.tagId ?? undefined,
    search: f.search || undefined,
    from: f.from || undefined,
    to: f.to || undefined,
    minAmount: parseAmount(f.minAmount) ?? undefined,
    maxAmount: parseAmount(f.maxAmount) ?? undefined,
    cleared: f.cleared ?? undefined,
    cursor: cursor ?? undefined,
    limit: 50,
  }
}

async function load(more = false) {
  loading.value = true
  error.value = null
  try {
    const page: Schemas['TransactionPage'] = await unwrap(
      api.GET('/api/transactions', { params: { query: apiQuery(more ? nextCursor.value : null) } }),
    )
    items.value = more ? [...items.value, ...page.items] : page.items
    nextCursor.value = page.nextCursor
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  } finally {
    loading.value = false
  }
}

let searchTimer: ReturnType<typeof setTimeout> | undefined
watch(
  filters,
  () => {
    clearTimeout(searchTimer)
    searchTimer = setTimeout(() => {
      writeRoute()
      void load()
    }, 250)
  },
  { deep: true },
)
watch(
  () => editor.revision,
  () => void Promise.all([load(), loadTags()]),
)

function resetFilters() {
  filters.value = {
    ...filters.value,
    categoryId: null,
    tagId: null,
    from: '',
    to: '',
    minAmount: '',
    maxAmount: '',
    cleared: null,
  }
}

async function toggleCleared(t: Transaction) {
  try {
    const updated = await unwrap(
      api.POST('/api/transactions/{id}/clear', {
        params: { path: { id: t.id } },
        body: { isCleared: !t.isCleared },
      }),
    )
    items.value = items.value.map((i) => (i.id === t.id ? updated : i))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Pointage impossible.'
  }
}

function title(t: Transaction): string {
  if (t.transferId) {
    const other = t.transferAccountId ? reference.accountById.get(t.transferAccountId)?.name : undefined
    return t.amount < 0 ? `Virement vers ${other ?? '?'}` : `Virement depuis ${other ?? '?'}`
  }
  return t.payeeName ?? t.label ?? reference.categoryLabel(t.lines[0]?.categoryId)
}

function subtitle(t: Transaction): string {
  const parts: string[] = []
  if (!t.transferId) {
    parts.push(
      t.lines.length > 1 ? `Ventilée (${t.lines.length})` : reference.categoryLabel(t.lines[0]?.categoryId),
    )
  }
  if (t.payeeName && t.label) parts.push(t.label)
  if (!filters.value.accountId) parts.push(reference.accountById.get(t.accountId)?.name ?? '')
  const lineTags = [...new Set(t.lines.flatMap((l) => l.tags))]
  if (lineTags.length) parts.push(lineTags.map((tag) => `#${tag}`).join(' '))
  const net = netCostText(t)
  if (net) parts.push(net)
  return parts.filter(Boolean).join(' · ')
}

function toggleSelecting() {
  selecting.value = !selecting.value
  selected.value = []
}

function toggleSelected(id: string) {
  selected.value = selected.value.includes(id)
    ? selected.value.filter((s) => s !== id)
    : [...selected.value, id]
}

function selectAll() {
  selected.value = selected.value.length === items.value.length ? [] : items.value.map((t) => t.id)
}

function openItem(t: Transaction) {
  if (selecting.value) toggleSelected(t.id)
  else if (auth.canWrite) editor.open('edit', t)
}

async function bulkDone(message: string) {
  success.value = message
  selected.value = []
  selecting.value = false
  await Promise.all([load(), loadTags()])
}

async function loadTags() {
  const { data } = await api.GET('/api/tags')
  tags.value = data ?? []
}

function avatar(t: Transaction) {
  if (t.transferId) return { icon: mdiSwapHorizontal, color: 'secondary' }
  const category = t.lines[0]?.categoryId ? reference.categoryById.get(t.lines[0].categoryId) : undefined
  return { icon: iconPath(category?.icon), color: category?.color ?? 'grey' }
}

onMounted(async () => {
  readRoute()
  await reference.load().catch(() => (error.value = 'Chargement impossible.'))
  await Promise.all([load(), loadTags()])
})
</script>

<template>
  <div class="d-flex flex-wrap align-center ga-2 mb-4">
    <h1 class="text-h5 font-weight-bold mr-auto">Opérations</h1>
    <v-btn
      v-if="auth.canWrite"
      variant="tonal"
      :color="selecting ? 'primary' : undefined"
      :prepend-icon="mdiCheckboxMarkedOutline"
      @click="toggleSelecting"
      >{{ selecting ? 'Terminer' : 'Sélectionner' }}</v-btn
    >
    <v-btn v-if="auth.canWrite" variant="tonal" :prepend-icon="mdiFileUploadOutline" :to="{ name: 'import' }"
      >Importer</v-btn
    >
    <!-- Export (H5) des opérations filtrées : téléchargement direct, la session suffit (lecture). -->
    <v-menu>
      <template #activator="{ props }">
        <v-btn v-bind="props" variant="tonal" :prepend-icon="mdiDownload">Exporter</v-btn>
      </template>
      <v-list density="compact">
        <v-list-item
          :href="transactionsExportUrl(filters, 'csv')"
          download
          title="CSV (Excel en français)"
          subtitle="Séparateur « ; », virgule décimale"
        />
        <v-list-item :href="transactionsExportUrl(filters, 'xlsx')" download title="Classeur Excel (.xlsx)" />
      </v-list>
    </v-menu>
  </div>

  <v-alert
    v-if="error"
    type="error"
    variant="tonal"
    class="mb-4"
    :text="error"
    closable
    @click:close="error = null"
  />
  <v-alert
    v-if="success"
    type="success"
    variant="tonal"
    class="mb-4"
    :text="success"
    closable
    @click:close="success = null"
  />

  <v-toolbar v-if="selecting" density="compact" color="primary" variant="tonal" rounded class="mb-3 px-2">
    <span class="text-body-2">{{ selected.length }} sélectionnée(s)</span>
    <v-spacer />
    <v-btn variant="text" @click="selectAll">{{
      selected.length === items.length && items.length ? 'Tout désélectionner' : 'Tout sélectionner'
    }}</v-btn>
    <v-btn
      variant="flat"
      color="primary"
      :prepend-icon="mdiPencilBoxMultipleOutline"
      :disabled="!selected.length"
      @click="bulkOpen = true"
      >Modifier</v-btn
    >
  </v-toolbar>

  <v-row dense class="mb-2">
    <v-col cols="12" sm="5">
      <v-text-field
        v-model="filters.search"
        :prepend-inner-icon="mdiMagnify"
        label="Rechercher"
        density="compact"
        hide-details
        clearable
        @click:clear="filters.search = ''"
      />
    </v-col>
    <v-col cols="9" sm="5">
      <v-select
        v-model="filters.accountId"
        :items="accountItems"
        label="Compte"
        density="compact"
        hide-details
        clearable
      />
    </v-col>
    <v-col cols="3" sm="2" class="d-flex align-center">
      <v-btn
        variant="tonal"
        block
        :prepend-icon="mdiFilterVariant"
        aria-label="Filtres"
        @click="filtersOpen = true"
      >
        <v-badge v-if="activeFilterCount" :content="activeFilterCount" inline color="primary" />
      </v-btn>
    </v-col>
  </v-row>

  <v-card v-if="items.length === 0 && !loading" class="pa-6 text-center text-medium-emphasis">
    Aucune opération.
    <span v-if="auth.canWrite">Utilisez le bouton « + » pour en saisir une.</span>
  </v-card>

  <v-card v-for="group in groups" :key="group.date" class="mb-3">
    <v-card-subtitle class="pt-2">{{ formatDayHeading(group.date) }}</v-card-subtitle>
    <v-list density="comfortable" class="py-0">
      <v-list-item
        v-for="t in group.transactions"
        :key="t.id"
        :data-testid="`transaction-${t.id}`"
        :disabled="false"
        :active="selecting && selected.includes(t.id)"
        @click="openItem(t)"
      >
        <template #prepend>
          <v-checkbox-btn
            v-if="selecting"
            :model-value="selected.includes(t.id)"
            :aria-label="`Sélectionner ${title(t)}`"
            class="mr-2"
            @click.stop="toggleSelected(t.id)"
          />
          <v-avatar :color="avatar(t).color" variant="tonal" size="36">
            <v-icon :icon="avatar(t).icon" size="20" />
          </v-avatar>
        </template>
        <v-list-item-title class="text-wrap">{{ title(t) }}</v-list-item-title>
        <v-list-item-subtitle>{{ subtitle(t) }}</v-list-item-subtitle>
        <template #append>
          <span class="font-weight-medium text-no-wrap" :class="t.amount > 0 ? 'text-success' : ''">
            {{ formatSignedAmount(t.amount) }}
          </span>
          <v-icon
            v-if="t.isReconciled"
            :icon="mdiLockOutline"
            color="success"
            size="small"
            class="mx-2"
            aria-label="Rapprochée"
            title="Rapprochée : verrouillée"
          />
          <v-btn
            v-else
            :icon="t.isCleared ? mdiCheckCircle : mdiCheckboxBlankCircleOutline"
            :color="t.isCleared ? 'success' : undefined"
            variant="text"
            size="small"
            :disabled="!auth.canWrite"
            :aria-label="t.isCleared ? 'Dépointer' : 'Pointer'"
            :aria-pressed="t.isCleared"
            @click.stop="toggleCleared(t)"
          />
        </template>
      </v-list-item>
    </v-list>
  </v-card>

  <div class="text-center my-4">
    <v-progress-circular v-if="loading" indeterminate color="primary" />
    <v-btn v-else-if="nextCursor" variant="tonal" @click="load(true)">Afficher plus</v-btn>
  </div>

  <v-navigation-drawer v-model="filtersOpen" location="right" temporary width="320">
    <div class="pa-4">
      <div class="text-h6 mb-4">Filtres</div>
      <v-autocomplete v-model="filters.categoryId" :items="categoryItems" label="Catégorie" clearable />
      <v-autocomplete v-model="filters.tagId" :items="tagItems" label="Tag" clearable />
      <v-text-field v-model="filters.from" label="Du" type="date" clearable />
      <v-text-field v-model="filters.to" label="Au" type="date" clearable />
      <v-row dense>
        <v-col cols="6">
          <v-text-field
            v-model="filters.minAmount"
            label="Montant min."
            suffix="€"
            inputmode="decimal"
            clearable
          />
        </v-col>
        <v-col cols="6">
          <v-text-field
            v-model="filters.maxAmount"
            label="Montant max."
            suffix="€"
            inputmode="decimal"
            clearable
          />
        </v-col>
      </v-row>
      <v-select v-model="filters.cleared" :items="clearedItems" label="Pointage" />
      <div class="d-flex ga-2">
        <v-btn variant="text" @click="resetFilters">Réinitialiser</v-btn>
        <v-spacer />
        <v-btn color="primary" variant="flat" @click="filtersOpen = false">Fermer</v-btn>
      </div>
    </div>
  </v-navigation-drawer>

  <BulkEditDialog v-model="bulkOpen" :ids="selected" @done="bulkDone" />
</template>
