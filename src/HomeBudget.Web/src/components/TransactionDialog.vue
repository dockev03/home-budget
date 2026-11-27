<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useDisplay } from 'vuetify'
import {
  mdiCallSplit,
  mdiClose,
  mdiContentCopy,
  mdiDeleteOutline,
  mdiHistory,
  mdiLockOutline,
  mdiPlus,
} from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { isNetworkError } from '@/api/offline'
import { useOfflineStore } from '@/stores/offline'
import { useReferenceStore } from '@/stores/reference'
import { useTransactionEditor, type Transaction } from '@/stores/transactionEditor'
import {
  formatAmount,
  formatDate,
  formatSignedAmount,
  parseAmount,
  roundCents,
  todayIso,
} from '@/utils/format'
import { cleanTags, correctionOf, isCorrection, recentShortcuts, type Correction } from '@/utils/learning'
import { netCostText } from '@/utils/reconciliation'
import { categoryKindTitle } from '@/utils/labels'
import { required } from '@/utils/rules'
import { authorText } from '@/utils/users'
import TransactionAttachments from '@/components/TransactionAttachments.vue'
import TransactionHistory from '@/components/TransactionHistory.vue'
import { useAuthStore } from '@/stores/auth'

type Kind = 'expense' | 'income' | 'transfer'
interface Line {
  categoryId: string | null
  amount: string
  memo: string
  tags: string[]
}

const emptyLine = (): Line => ({ categoryId: null, amount: '', memo: '', tags: [] })

const LAST_ACCOUNT_KEY = 'hb.lastAccountId'

const editor = useTransactionEditor()
const auth = useAuthStore()
const offline = useOfflineStore()
const reference = useReferenceStore()
const { smAndDown } = useDisplay()

const kind = ref<Kind>('expense')
const accountId = ref<string | null>(null)
const toAccountId = ref<string | null>(null)
const date = ref(todayIso())
const payeeName = ref<string | null>(null)
const label = ref('')
const notes = ref('')
const isCleared = ref(false)
const split = ref(false)
const lines = ref<Line[]>([emptyLine()])
const transferCategoryId = ref<string | null>(null)
const transferVersion = ref<number | null>(null)

const payeeItems = ref<string[]>([])
const tagItems = ref<string[]>([])
const shortcuts = ref<Transaction[]>([])
/** Raccourcis préchargés, affichés dès l'ouverture du formulaire de création. */
const recentCache = ref<Transaction[]>([])
/** Catégorie posée d'après l'historique du tiers (I2), tant que l'utilisateur ne l'a pas changée. */
const suggestedCategoryId = ref<string | null>(null)
/** Opérations semblables proposées après une correction (I4), et la correction à leur appliquer. */
const similar = ref<Transaction[] | null>(null)
const correction = ref<Correction | null>(null)
const applying = ref(false)
const valid = ref(false)
const saving = ref(false)
const deleting = ref(false)
const confirmDelete = ref(false)
const error = ref<string | null>(null)

const source = computed(() => editor.transaction)
const isEdit = computed(() => editor.mode === 'edit' && !!source.value)
/** Opération rapprochée (A5, R7) : seuls ses liens de remboursement et d'avance restent modifiables. */
const locked = computed(() => isEdit.value && !!source.value?.isReconciled)
/** Auteur et dates de création et de modification (K2). */
const author = computed(() =>
  isEdit.value && source.value ? authorText(source.value, reference.userNames) : null,
)
/** Liens d'un revenu existant (B11, B12) : la dépense remboursée et l'avance soldée. */
const showLinks = computed(() => isEdit.value && kind.value === 'income')
const reimbursesId = ref<string | null>(null)
const advanceId = ref<string | null>(null)
const expenseItems = ref<{ value: string; title: string }[]>([])
const advanceItems = ref<{ value: string; title: string }[]>([])
const linksChanged = computed(
  () =>
    !!source.value &&
    (reimbursesId.value !== (source.value.reimbursesId ?? null) ||
      advanceId.value !== (source.value.advanceId ?? null)),
)
/** Mois concerné (P2), au format « AAAA-MM » ; vide = mois de la date. */
const referenceMonth = ref('')
/** Le mois concerné ne sert qu'aux frais de garde : proposé pour leurs catégories, ou s'il est déjà renseigné. */
const showReferenceMonth = computed(
  () =>
    kind.value !== 'transfer' &&
    (!!referenceMonth.value ||
      lines.value.some((l) => {
        const role = reference.categories.find((c) => c.id === l.categoryId)?.familyRole
        return role === 'Childcare' || role === 'ChildcareAid'
      })),
)
const netCost = computed(() => (isEdit.value && source.value ? netCostText(source.value) : null))
const title = computed(() => {
  if (isEdit.value) return kind.value === 'transfer' ? 'Modifier le virement' : "Modifier l'opération"
  return editor.mode === 'duplicate' ? 'Dupliquer' : 'Nouvelle opération'
})

const sign = computed(() => (kind.value === 'income' ? 1 : -1))
const accounts = computed(() =>
  reference.accounts
    .filter((a) => !a.isArchived || a.id === accountId.value || a.id === toAccountId.value)
    .map((a) => ({ value: a.id, title: a.name })),
)

/** Catégories actives, celles du type de l'opération en premier. */
const categoryItems = computed(() => {
  const preferred = kind.value === 'income' ? ['Income'] : ['Variable', 'Fixed', 'Savings']
  const rank = (k: string) => (preferred.includes(k) ? preferred.indexOf(k) : 10)
  return reference.categories
    .filter((c) => !c.isArchived || lines.value.some((l) => l.categoryId === c.id))
    .map((c) => ({ value: c.id, title: reference.categoryLabel(c.id), kind: c.kind }))
    .sort((a, b) => rank(a.kind) - rank(b.kind))
})
const savingsCategories = computed(() =>
  reference.categories
    .filter((c) => c.kind === 'Savings' && !c.isArchived)
    .map((c) => ({ value: c.id, title: reference.categoryLabel(c.id) })),
)

const total = computed(() =>
  roundCents(lines.value.reduce((sum, l) => sum + (parseAmount(l.amount) ?? 0), 0)),
)

const amountRule = (v: string) => {
  const amount = parseAmount(v)
  if (amount === null) return 'Montant invalide (2 décimales au plus).'
  return amount !== 0 || 'Le montant ne peut pas être nul.'
}
const positiveRule = (v: string) => (parseAmount(v) ?? 0) > 0 || 'Montant positif attendu.'
const differentAccount = (v: string | null) => v !== accountId.value || 'Choisissez deux comptes différents.'

function readLastAccount(): string | null {
  try {
    return localStorage.getItem(LAST_ACCOUNT_KEY)
  } catch {
    return null
  }
}

function rememberAccount(id: string) {
  try {
    localStorage.setItem(LAST_ACCOUNT_KEY, id)
  } catch {
    // Stockage indisponible (navigation privée) : simple confort perdu.
  }
}

function defaultAccount(): string | null {
  const active = reference.activeAccounts
  const candidates = [editor.defaultAccountId, readLastAccount()]
  return candidates.find((id) => id && active.some((a) => a.id === id)) ?? active[0]?.id ?? null
}

const amountText = (value: number) => String(Math.abs(value)).replace('.', ',')

async function reset() {
  error.value = null
  confirmDelete.value = false
  transferVersion.value = null
  similar.value = null
  correction.value = null
  suggestedCategoryId.value = null
  reimbursesId.value = null
  advanceId.value = null
  referenceMonth.value = ''
  expenseItems.value = []
  advanceItems.value = []
  await reference.load()
  void loadTags()
  const t = source.value
  const duplicate = editor.mode === 'duplicate'

  if (!t) {
    shortcuts.value = recentCache.value
    kind.value = 'expense'
    accountId.value = defaultAccount()
    toAccountId.value = null
    date.value = todayIso()
    payeeName.value = null
    label.value = ''
    notes.value = ''
    isCleared.value = false
    split.value = false
    lines.value = [emptyLine()]
    transferCategoryId.value = null
    return
  }
  shortcuts.value = []

  date.value = duplicate ? todayIso() : t.date
  label.value = t.label ?? ''
  notes.value = t.notes ?? ''
  isCleared.value = duplicate ? false : t.isCleared

  if (t.transferId) {
    const transfer = await unwrap(
      api.GET('/api/transfers/{transferId}', { params: { path: { transferId: t.transferId } } }),
    )
    kind.value = 'transfer'
    accountId.value = transfer.fromAccountId
    toAccountId.value = transfer.toAccountId
    lines.value = [{ ...emptyLine(), amount: amountText(transfer.amount) }]
    transferCategoryId.value = transfer.categoryId
    transferVersion.value = transfer.version
    split.value = false
    payeeName.value = null
    return
  }

  kind.value = t.amount > 0 ? 'income' : 'expense'
  accountId.value = t.accountId
  payeeName.value = t.payeeName
  const s = t.amount > 0 ? 1 : -1
  // Montants affichés dans le sens de l'opération : une ligne de sens contraire reste négative.
  lines.value = t.lines.map((l) => ({
    categoryId: l.categoryId,
    amount: String(roundCents(l.amount * s)).replace('.', ','),
    memo: l.memo ?? '',
    tags: [...l.tags],
  }))
  split.value = t.lines.length > 1
  referenceMonth.value = duplicate ? '' : (t.referenceMonth?.slice(0, 7) ?? '')
  reimbursesId.value = duplicate ? null : (t.reimbursesId ?? null)
  advanceId.value = duplicate ? null : (t.advanceId ?? null)
  if (!duplicate && t.amount > 0) void loadLinkItems(t)
}

const expenseTitle = (e: Transaction) =>
  `${formatDate(e.date)} · ${e.payeeName ?? e.label ?? reference.categoryLabel(e.lines[0]?.categoryId)} · ${formatSignedAmount(e.amount)}`

/** Dépenses remboursables (dont celle déjà liée) et avances ouvertes (dont celle déjà liée). */
async function loadLinkItems(t: Transaction, search?: string) {
  const [expenses, advances] = await Promise.all([
    api.GET('/api/transactions/{id}/reimbursable', { params: { path: { id: t.id }, query: { search } } }),
    search === undefined ? api.GET('/api/advances') : Promise.resolve(null),
  ])
  const list = expenses.data ?? []
  if (t.reimbursesId && !list.some((e) => e.id === t.reimbursesId)) {
    const { data } = await api.GET('/api/transactions/{id}', { params: { path: { id: t.reimbursesId } } })
    if (data) list.unshift(data)
  }
  expenseItems.value = list.map((e) => ({ value: e.id, title: expenseTitle(e) }))
  if (advances?.data) {
    advanceItems.value = advances.data
      .filter((a) => a.status === 'Open' || a.id === t.advanceId)
      .map((a) => ({ value: a.id, title: `${a.label} · reste ${formatAmount(a.remaining)}` }))
  }
}

let expenseTimer: ReturnType<typeof setTimeout> | undefined
function searchExpenses(text: string | null) {
  const t = source.value
  if (!t || !showLinks.value) return
  // La recherche part du texte saisi, pas du libellé de l'élément choisi.
  if (text && expenseItems.value.some((i) => i.title === text)) return
  clearTimeout(expenseTimer)
  expenseTimer = setTimeout(() => void loadLinkItems(t, text ?? ''), 250)
}

async function saveLinks(id: string) {
  await unwrap(
    api.PUT('/api/transactions/{id}/links', {
      params: { path: { id } },
      body: { reimbursesId: reimbursesId.value, advanceId: advanceId.value },
    }),
  )
}

watch(
  () => editor.visible,
  (open) => {
    // À la fermeture, les raccourcis sont rechargés pour la prochaine saisie : ils sont prêts dès l'ouverture,
    // sans décaler les champs pendant la frappe.
    if (open) void reset()
    else void loadShortcuts()
  },
)

onMounted(() => void loadShortcuts())

async function loadTags() {
  const { data } = await api.GET('/api/tags')
  tagItems.value = (data ?? []).map((t) => t.name)
}

/** Raccourcis « refaire une opération récente » (L5), à la création seulement. */
async function loadShortcuts() {
  const { data } = await api.GET('/api/transactions', { params: { query: { limit: 50 } } })
  recentCache.value = recentShortcuts(data?.items ?? [])
}

/** Repart d'une opération récente : mêmes compte, tiers, catégorie et montant, datée du jour. */
function useShortcut(t: Transaction) {
  editor.mode = 'duplicate'
  editor.transaction = t
  void reset()
}

/** Catégorie habituelle du tiers choisi (I2), si aucune catégorie n'est encore choisie. */
let suggestTimer: ReturnType<typeof setTimeout> | undefined
watch(payeeName, (name) => {
  clearTimeout(suggestTimer)
  if (isEdit.value || kind.value === 'transfer' || split.value || !name?.trim()) return
  const line = lines.value[0]
  if (line?.categoryId && line.categoryId !== suggestedCategoryId.value) return
  suggestTimer = setTimeout(async () => {
    const { data } = await api.GET('/api/suggestions', { params: { query: { payeeName: name } } })
    const current = lines.value[0]
    if (!data?.categoryId || !current || payeeName.value !== name) return
    if (current.categoryId && current.categoryId !== suggestedCategoryId.value) return
    current.categoryId = data.categoryId
    suggestedCategoryId.value = data.categoryId
  }, 300)
})

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

function toggleSplit() {
  split.value = !split.value
  if (split.value && lines.value.length === 1) {
    lines.value.push(emptyLine())
  } else if (!split.value) {
    lines.value = [
      { ...lines.value[0]!, amount: total.value ? amountText(total.value) : lines.value[0]!.amount },
    ]
  }
}

/** Repart de l'opération affichée pour en créer une nouvelle, datée du jour (B9). */
function duplicate() {
  editor.mode = 'duplicate'
  date.value = todayIso()
  isCleared.value = false
  confirmDelete.value = false
}

function close() {
  editor.visible = false
}

async function save() {
  if (locked.value && source.value) {
    await saveLinksOnly(source.value.id)
    return
  }
  if (!valid.value || !accountId.value) return
  saving.value = true
  error.value = null
  try {
    if (kind.value === 'transfer') {
      const body = {
        fromAccountId: accountId.value,
        toAccountId: toAccountId.value!,
        date: date.value,
        amount: parseAmount(lines.value[0]!.amount)!,
        label: label.value || null,
        notes: notes.value || null,
        categoryId: transferCategoryId.value,
      }
      if (isEdit.value && source.value?.transferId) {
        await unwrap(
          api.PUT('/api/transfers/{transferId}', {
            params: { path: { transferId: source.value.transferId } },
            headers: ifMatch(transferVersion.value ?? 0),
            body,
          }),
        )
      } else {
        await unwrap(api.POST('/api/transfers', { body }))
      }
    } else {
      const body = {
        accountId: accountId.value,
        date: date.value,
        payeeName: payeeName.value || null,
        label: label.value || null,
        notes: notes.value || null,
        isCleared: isCleared.value,
        referenceMonth:
          showReferenceMonth.value && referenceMonth.value ? `${referenceMonth.value}-01` : null,
        lines: lines.value.map((l) => ({
          categoryId: l.categoryId,
          amount: roundCents(sign.value * parseAmount(l.amount)!),
          memo: l.memo || null,
          tags: cleanTags(l.tags),
        })),
      }
      if (isEdit.value && source.value) {
        const updated = await unwrap(
          api.PUT('/api/transactions/{id}', {
            params: { path: { id: source.value.id } },
            headers: ifMatch(source.value.version),
            body,
          }),
        )
        if (showLinks.value && linksChanged.value) await saveLinks(updated.id)
        if (await offerSimilar(source.value, updated)) {
          rememberAccount(accountId.value)
          await reference.loadAccounts()
          return
        }
      } else if (await createOrQueue(body)) {
        return
      }
    }
    rememberAccount(accountId.value)
    await reference.loadAccounts()
    editor.saved()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

/**
 * Crée l'opération, ou la met en file d'attente sans réseau (L4) : elle sera envoyée au retour de la connexion.
 * @returns vrai si l'opération a été mise en file (le formulaire est alors fermé).
 */
async function createOrQueue(body: Schemas['TransactionRequest']): Promise<boolean> {
  if (offline.online) {
    try {
      await unwrap(api.POST('/api/transactions', { body }))
      return false
    } catch (e) {
      if (!isNetworkError(e)) throw e
    }
  }
  await offline.enqueue(body)
  rememberAccount(body.accountId)
  editor.saved()
  return true
}

/** Opération rapprochée : seuls les liens sont enregistrés. */
async function saveLinksOnly(id: string) {
  if (!linksChanged.value) {
    close()
    return
  }
  saving.value = true
  error.value = null
  try {
    await saveLinks(id)
    editor.saved()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

/**
 * Après une correction du tiers ou de la catégorie (I4) : cherche les opérations de même libellé bancaire à
 * corriger de la même façon. Renvoie vrai si elles sont proposées (le formulaire reste alors ouvert).
 */
async function offerSimilar(before: Transaction, after: Transaction): Promise<boolean> {
  const change = correctionOf(after)
  if (!isCorrection(correctionOf(before), change)) return false
  const { data } = await api.GET('/api/transactions/{id}/similar', { params: { path: { id: after.id } } })
  if (!data?.length) return false
  correction.value = change
  similar.value = data
  return true
}

async function applySimilar() {
  if (!similar.value || !correction.value) return
  applying.value = true
  error.value = null
  try {
    await unwrap(
      api.POST('/api/transactions/bulk', {
        body: {
          ids: similar.value.map((t) => t.id),
          category: correction.value.categoryId ? { categoryId: correction.value.categoryId } : null,
          payeeName: correction.value.payeeName,
          accountId: null,
          isCleared: null,
          addTags: null,
          removeTags: null,
        },
      }),
    )
    editor.saved()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Correction impossible.'
  } finally {
    applying.value = false
  }
}

function similarTitle(t: Transaction): string {
  return t.payeeName ?? t.label ?? reference.categoryLabel(t.lines[0]?.categoryId)
}

async function remove() {
  const t = source.value
  if (!t) return
  deleting.value = true
  error.value = null
  try {
    if (t.transferId) {
      await unwrap(
        api.DELETE('/api/transfers/{transferId}', {
          params: { path: { transferId: t.transferId } },
          headers: ifMatch(transferVersion.value ?? 0),
        }),
      )
    } else {
      await unwrap(
        api.DELETE('/api/transactions/{id}', { params: { path: { id: t.id } }, headers: ifMatch(t.version) }),
      )
    }
    await reference.loadAccounts()
    editor.saved()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}
</script>

<template>
  <v-dialog v-model="editor.visible" :fullscreen="smAndDown" max-width="640" scrollable>
    <v-card>
      <v-toolbar density="comfortable" color="surface">
        <v-btn :icon="mdiClose" aria-label="Fermer" @click="close" />
        <v-toolbar-title>{{ title }}</v-toolbar-title>
      </v-toolbar>

      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

        <div v-if="similar" data-testid="similar">
          <p class="mb-2">
            Opération enregistrée. {{ similar.length }} autre(s) opération(s) au même libellé bancaire ont un
            autre tiers ou une autre catégorie. Leur appliquer la même correction ?
          </p>
          <p class="text-body-2 text-medium-emphasis mb-2">
            <template v-if="correction?.payeeName">Tiers : {{ correction.payeeName }}. </template>
            <template v-if="correction?.categoryId"
              >Catégorie : {{ reference.categoryLabel(correction.categoryId) }}.</template
            >
          </p>
          <v-list density="compact" class="border rounded">
            <v-list-item
              v-for="t in similar.slice(0, 20)"
              :key="t.id"
              :title="similarTitle(t)"
              :subtitle="`${formatDate(t.date)} · ${reference.categoryLabel(t.lines[0]?.categoryId)}`"
            >
              <template #append>
                <span class="text-no-wrap">{{ formatSignedAmount(t.amount) }}</span>
              </template>
            </v-list-item>
            <v-list-item v-if="similar.length > 20" :subtitle="`… et ${similar.length - 20} autre(s)`" />
          </v-list>
        </div>

        <template v-else>
          <v-alert
            v-if="locked"
            type="info"
            variant="tonal"
            class="mb-4"
            :icon="mdiLockOutline"
            data-testid="locked"
            text="Opération rapprochée : elle ne peut plus être modifiée. Annulez le rapprochement du compte pour la changer."
          />
          <v-alert
            v-if="netCost"
            type="info"
            variant="tonal"
            density="compact"
            class="mb-4"
            :text="netCost"
          />
          <div v-if="shortcuts.length" class="mb-3" data-testid="shortcuts">
            <div class="text-caption text-medium-emphasis mb-1">Refaire une opération récente</div>
            <v-chip-group column>
              <v-chip
                v-for="t in shortcuts"
                :key="t.id"
                :prepend-icon="mdiHistory"
                size="small"
                @click="useShortcut(t)"
              >
                {{ similarTitle(t) }} · {{ formatSignedAmount(t.amount) }}
              </v-chip>
            </v-chip-group>
          </div>

          <v-btn-toggle
            v-model="kind"
            mandatory
            divided
            variant="outlined"
            color="primary"
            class="mb-4 w-100"
            :disabled="isEdit"
          >
            <v-btn value="expense" class="flex-grow-1">Dépense</v-btn>
            <v-btn value="income" class="flex-grow-1">Revenu</v-btn>
            <v-btn value="transfer" class="flex-grow-1">Virement</v-btn>
          </v-btn-toggle>

          <v-form v-model="valid" :disabled="locked" @submit.prevent="save">
            <v-row dense>
              <v-col cols="12" sm="6">
                <v-text-field
                  v-if="!split"
                  v-model="lines[0]!.amount"
                  label="Montant"
                  suffix="€"
                  inputmode="decimal"
                  v-autofocus
                  :rules="kind === 'transfer' ? [amountRule, positiveRule] : [amountRule]"
                />
                <v-text-field v-else :model-value="formatAmount(total)" label="Montant total" readonly />
              </v-col>
              <v-col cols="12" sm="6">
                <v-text-field v-model="date" label="Date" type="date" :rules="[required]" />
              </v-col>
            </v-row>

            <v-text-field
              v-if="showReferenceMonth"
              v-model="referenceMonth"
              label="Mois concerné"
              type="month"
              hint="Mois de garde auquel se rapporte ce paiement ou cette aide, s’il diffère de celui de la date"
              persistent-hint
              clearable
              class="mb-4"
              data-testid="reference-month"
            />

            <v-select
              v-model="accountId"
              :items="accounts"
              :label="kind === 'transfer' ? 'Depuis le compte' : 'Compte'"
              :rules="[required]"
            />

            <template v-if="kind === 'transfer'">
              <v-select
                v-model="toAccountId"
                :items="accounts"
                label="Vers le compte"
                :rules="[required, differentAccount]"
              />
              <v-select
                v-model="transferCategoryId"
                :items="savingsCategories"
                label="Catégorie d'épargne (facultatif)"
                clearable
                :no-data-text="'Aucune catégorie de type épargne'"
              />
            </template>

            <template v-else>
              <v-combobox
                v-model="payeeName"
                :items="payeeItems"
                label="Tiers"
                clearable
                @update:search="searchPayees"
                @focus="searchPayees(payeeName)"
              />

              <v-autocomplete
                v-if="!split"
                v-model="lines[0]!.categoryId"
                :items="categoryItems"
                label="Catégorie"
                clearable
                :hint="
                  suggestedCategoryId && lines[0]!.categoryId === suggestedCategoryId
                    ? 'Catégorie habituelle de ce tiers'
                    : undefined
                "
                persistent-hint
              >
                <template #item="{ props: itemProps, item }">
                  <v-list-item v-bind="itemProps" :subtitle="categoryKindTitle(item.kind)" />
                </template>
              </v-autocomplete>
              <v-combobox
                v-if="!split"
                v-model="lines[0]!.tags"
                :items="tagItems"
                label="Tags"
                multiple
                chips
                closable-chips
                clearable
                :delimiters="[',']"
              />

              <v-card v-else variant="outlined" class="mb-4 pa-2">
                <div class="text-subtitle-2 mb-2 px-1">Ventilation</div>
                <v-row v-for="(line, index) in lines" :key="index" dense align="center">
                  <v-col cols="12" sm="6">
                    <v-autocomplete
                      v-model="line.categoryId"
                      :items="categoryItems"
                      :label="`Catégorie ${index + 1}`"
                      density="compact"
                      hide-details="auto"
                      clearable
                    />
                  </v-col>
                  <v-col cols="5" sm="3">
                    <v-text-field
                      v-model="line.amount"
                      label="Montant"
                      suffix="€"
                      inputmode="decimal"
                      density="compact"
                      hide-details="auto"
                      :rules="[amountRule]"
                    />
                  </v-col>
                  <v-col cols="5" sm="2">
                    <v-text-field
                      v-model="line.memo"
                      label="Note"
                      density="compact"
                      hide-details
                      maxlength="200"
                    />
                  </v-col>
                  <v-col cols="2" sm="1" class="text-right">
                    <v-btn
                      :icon="mdiDeleteOutline"
                      variant="text"
                      size="small"
                      :disabled="lines.length <= 1"
                      :aria-label="`Retirer la ligne ${index + 1}`"
                      @click="lines.splice(index, 1)"
                    />
                  </v-col>
                  <v-col cols="12" class="pb-3">
                    <v-combobox
                      v-model="line.tags"
                      :items="tagItems"
                      :label="`Tags ${index + 1}`"
                      density="compact"
                      hide-details
                      multiple
                      chips
                      closable-chips
                      :delimiters="[',']"
                    />
                  </v-col>
                </v-row>
                <v-btn
                  :prepend-icon="mdiPlus"
                  variant="text"
                  size="small"
                  class="mt-2"
                  @click="lines.push(emptyLine())"
                >
                  Ajouter une ligne
                </v-btn>
              </v-card>

              <v-btn
                :prepend-icon="mdiCallSplit"
                variant="text"
                size="small"
                class="mb-4"
                @click="toggleSplit"
              >
                {{ split ? 'Ne pas ventiler' : 'Ventiler sur plusieurs catégories' }}
              </v-btn>
            </template>

            <v-text-field v-model="label" label="Libellé" maxlength="200" />
            <v-textarea v-model="notes" label="Note" rows="2" auto-grow maxlength="2000" />
            <v-checkbox v-if="kind !== 'transfer'" v-model="isCleared" label="Pointée" hide-details />
            <button type="submit" hidden />
          </v-form>

          <div v-if="showLinks" class="mt-4" data-testid="links">
            <v-autocomplete
              v-model="reimbursesId"
              :items="expenseItems"
              label="Rembourse la dépense"
              hint="La dépense affichera son coût net"
              persistent-hint
              clearable
              no-filter
              no-data-text="Aucune dépense trouvée"
              @update:search="searchExpenses"
            />
            <v-select
              v-model="advanceId"
              :items="advanceItems"
              label="Solde l’avance"
              clearable
              no-data-text="Aucune avance ouverte"
            />
          </div>
        </template>
        <div v-if="author" class="text-caption text-medium-emphasis mt-2" data-testid="author">
          {{ author }}
        </div>
        <!-- B7 : pièces jointes, y compris d'une opération rapprochée (elles ne changent ni montant ni date). -->
        <TransactionAttachments
          v-if="isEdit && source"
          :transaction-id="source.id"
          :can-write="auth.canWrite"
          @changed="editor.revision++"
        />
        <TransactionHistory v-if="isEdit && source" :transaction-id="source.id" />
      </v-card-text>

      <v-card-actions v-if="similar">
        <v-spacer />
        <v-btn @click="editor.saved()">Non merci</v-btn>
        <v-btn color="primary" variant="flat" :loading="applying" @click="applySimilar"
          >Appliquer aux {{ similar.length }} opération(s)</v-btn
        >
      </v-card-actions>
      <v-card-actions v-else>
        <template v-if="isEdit">
          <v-btn v-if="!confirmDelete && !locked" color="error" @click="confirmDelete = true"
            >Supprimer</v-btn
          >
          <v-btn v-else-if="confirmDelete" color="error" variant="flat" :loading="deleting" @click="remove"
            >Confirmer la suppression</v-btn
          >
          <v-btn :prepend-icon="mdiContentCopy" @click="duplicate">Dupliquer</v-btn>
        </template>
        <v-spacer />
        <v-btn @click="close">Annuler</v-btn>
        <v-btn
          color="primary"
          variant="flat"
          :loading="saving"
          :disabled="locked ? !linksChanged : !valid"
          @click="save"
          >Enregistrer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
