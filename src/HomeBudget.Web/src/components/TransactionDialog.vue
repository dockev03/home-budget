<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiCallSplit, mdiClose, mdiContentCopy, mdiDeleteOutline, mdiPlus } from '@mdi/js'
import { api } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useReferenceStore } from '@/stores/reference'
import { useTransactionEditor } from '@/stores/transactionEditor'
import { formatAmount, parseAmount, roundCents, todayIso } from '@/utils/format'
import { categoryKindTitle } from '@/utils/labels'
import { required } from '@/utils/rules'

type Kind = 'expense' | 'income' | 'transfer'
interface Line {
  categoryId: string | null
  amount: string
  memo: string
}

const LAST_ACCOUNT_KEY = 'hb.lastAccountId'

const editor = useTransactionEditor()
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
const lines = ref<Line[]>([{ categoryId: null, amount: '', memo: '' }])
const transferCategoryId = ref<string | null>(null)
const transferVersion = ref<number | null>(null)

const payeeItems = ref<string[]>([])
const valid = ref(false)
const saving = ref(false)
const deleting = ref(false)
const confirmDelete = ref(false)
const error = ref<string | null>(null)

const source = computed(() => editor.transaction)
const isEdit = computed(() => editor.mode === 'edit' && !!source.value)
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
  await reference.load()
  const t = source.value
  const duplicate = editor.mode === 'duplicate'

  if (!t) {
    kind.value = 'expense'
    accountId.value = defaultAccount()
    toAccountId.value = null
    date.value = todayIso()
    payeeName.value = null
    label.value = ''
    notes.value = ''
    isCleared.value = false
    split.value = false
    lines.value = [{ categoryId: null, amount: '', memo: '' }]
    transferCategoryId.value = null
    return
  }

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
    lines.value = [{ categoryId: null, amount: amountText(transfer.amount), memo: '' }]
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
  }))
  split.value = t.lines.length > 1
}

watch(
  () => editor.visible,
  (open) => {
    if (open) void reset()
  },
)

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
    lines.value.push({ categoryId: null, amount: '', memo: '' })
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
        lines: lines.value.map((l) => ({
          categoryId: l.categoryId,
          amount: roundCents(sign.value * parseAmount(l.amount)!),
          memo: l.memo || null,
        })),
      }
      if (isEdit.value && source.value) {
        await unwrap(
          api.PUT('/api/transactions/{id}', {
            params: { path: { id: source.value.id } },
            headers: ifMatch(source.value.version),
            body,
          }),
        )
      } else {
        await unwrap(api.POST('/api/transactions', { body }))
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

        <v-form v-model="valid" @submit.prevent="save">
          <v-row dense>
            <v-col cols="12" sm="6">
              <v-text-field
                v-if="!split"
                v-model="lines[0]!.amount"
                label="Montant"
                suffix="€"
                inputmode="decimal"
                autofocus
                :rules="kind === 'transfer' ? [amountRule, positiveRule] : [amountRule]"
              />
              <v-text-field v-else :model-value="formatAmount(total)" label="Montant total" readonly />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field v-model="date" label="Date" type="date" :rules="[required]" />
            </v-col>
          </v-row>

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
            >
              <template #item="{ props: itemProps, item }">
                <v-list-item v-bind="itemProps" :subtitle="categoryKindTitle(item.kind)" />
              </template>
            </v-autocomplete>

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
              </v-row>
              <v-btn
                :prepend-icon="mdiPlus"
                variant="text"
                size="small"
                class="mt-2"
                @click="lines.push({ categoryId: null, amount: '', memo: '' })"
              >
                Ajouter une ligne
              </v-btn>
            </v-card>

            <v-btn :prepend-icon="mdiCallSplit" variant="text" size="small" class="mb-4" @click="toggleSplit">
              {{ split ? 'Ne pas ventiler' : 'Ventiler sur plusieurs catégories' }}
            </v-btn>
          </template>

          <v-text-field v-model="label" label="Libellé" maxlength="200" />
          <v-textarea v-model="notes" label="Note" rows="2" auto-grow maxlength="2000" />
          <v-checkbox v-if="kind !== 'transfer'" v-model="isCleared" label="Pointée" hide-details />
          <button type="submit" hidden />
        </v-form>
      </v-card-text>

      <v-card-actions>
        <template v-if="isEdit">
          <v-btn v-if="!confirmDelete" color="error" @click="confirmDelete = true">Supprimer</v-btn>
          <v-btn v-else color="error" variant="flat" :loading="deleting" @click="remove"
            >Confirmer la suppression</v-btn
          >
          <v-btn :prepend-icon="mdiContentCopy" @click="duplicate">Dupliquer</v-btn>
        </template>
        <v-spacer />
        <v-btn @click="close">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save"
          >Enregistrer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
