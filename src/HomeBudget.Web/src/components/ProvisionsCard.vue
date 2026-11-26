<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiPlus } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore } from '@/stores/reference'
import { periodLabel } from '@/utils/budget'
import { formatAmount, formatDate, parseAmount, roundCents, todayIso } from '@/utils/format'
import { required } from '@/utils/rules'

/** Provisions (D11) : dépenses annuelles ou ponctuelles lissées dans le budget de chaque période. */
type Provision = Schemas['ProvisionDto']

const emit = defineEmits<{ changed: [] }>()
const auth = useAuthStore()
const reference = useReferenceStore()

const provisions = ref<Provision[]>([])
const error = ref<string | null>(null)

const dialog = ref(false)
const editing = ref<Provision | null>(null)
const label = ref('')
const categoryId = ref<string | null>(null)
const amount = ref('')
const dueDate = ref(todayIso())
const isAnnual = ref(true)
const startDate = ref(todayIso())
const valid = ref(false)
const saving = ref(false)
const deleting = ref(false)
const confirmDelete = ref(false)
const formError = ref<string | null>(null)

const categoryItems = computed(() =>
  reference.categories
    .filter(
      (c) => (c.kind === 'Fixed' || c.kind === 'Variable') && (!c.isArchived || c.id === categoryId.value),
    )
    .map((c) => ({ value: c.id, title: reference.categoryLabel(c.id) })),
)
const amountRule = (v: string) => {
  const value = parseAmount(v)
  return (value !== null && value > 0) || 'Montant positif, 2 décimales au plus.'
}
const dueRule = (v: string) =>
  isAnnual.value || !v || v >= startDate.value.slice(0, 7) || 'L’échéance doit suivre le début.'

async function load() {
  try {
    await reference.load()
    provisions.value = await unwrap(api.GET('/api/provisions'))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

function describe(p: Provision): string {
  const kind = p.isAnnual ? 'Chaque année' : 'Une fois'
  const due = p.dueKey ? `échéance en ${periodLabel(p.dueKey).toLowerCase()}` : 'terminée'
  return `${kind} · ${formatAmount(p.amount)} · ${due} · ${reference.categoryLabel(p.categoryId)}`
}

function open(p: Provision | null) {
  if (!auth.canWrite) return
  editing.value = p
  formError.value = null
  confirmDelete.value = false
  label.value = p?.label ?? ''
  categoryId.value = p?.categoryId ?? null
  amount.value = p ? String(p.amount).replace('.', ',') : ''
  dueDate.value = p?.dueDate ?? todayIso()
  isAnnual.value = p?.isAnnual ?? true
  startDate.value = p?.startDate ?? todayIso()
  dialog.value = true
}

async function save() {
  if (!valid.value || !categoryId.value) return
  saving.value = true
  formError.value = null
  const body = {
    label: label.value.trim(),
    categoryId: categoryId.value,
    amount: roundCents(parseAmount(amount.value)!),
    dueDate: dueDate.value,
    isAnnual: isAnnual.value,
    startDate: startDate.value,
  }
  try {
    if (editing.value) {
      await unwrap(
        api.PUT('/api/provisions/{id}', {
          params: { path: { id: editing.value.id } },
          headers: ifMatch(editing.value.version),
          body,
        }),
      )
    } else {
      await unwrap(api.POST('/api/provisions', { body }))
    }
    dialog.value = false
    await load()
    emit('changed')
  } catch (e) {
    formError.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove() {
  const p = editing.value
  if (!p) return
  deleting.value = true
  formError.value = null
  try {
    await unwrap(
      api.DELETE('/api/provisions/{id}', { params: { path: { id: p.id } }, headers: ifMatch(p.version) }),
    )
    dialog.value = false
    await load()
    emit('changed')
  } catch (e) {
    formError.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}

onMounted(load)
</script>

<template>
  <v-card class="mb-4" data-testid="provisions">
    <v-card-title class="d-flex align-center">
      <span class="mr-auto">Provisions</span>
      <v-btn v-if="auth.canWrite" :prepend-icon="mdiPlus" variant="text" color="primary" @click="open(null)">
        Ajouter
      </v-btn>
    </v-card-title>
    <v-card-subtitle class="text-wrap">
      Une dépense annuelle ou ponctuelle mise de côté chaque mois : la part réservée entre dans le budget, et
      le mois de l’échéance dispose du montant entier.
    </v-card-subtitle>
    <v-alert v-if="error" type="error" variant="tonal" class="ma-4" :text="error" />
    <v-list lines="two">
      <v-list-item
        v-for="p in provisions"
        :key="p.id"
        :title="p.label"
        :subtitle="describe(p)"
        :disabled="!auth.canWrite"
        @click="open(p)"
      >
        <template #append>
          <div class="text-right">
            <div class="font-weight-medium text-no-wrap">{{ formatAmount(p.share) }} / mois</div>
            <div class="text-caption text-medium-emphasis text-no-wrap">
              {{ formatAmount(p.saved) }} de côté
            </div>
          </div>
        </template>
      </v-list-item>
      <v-list-item v-if="provisions.length === 0" class="text-medium-emphasis" title="Aucune provision." />
    </v-list>
  </v-card>

  <v-dialog v-model="dialog" max-width="520">
    <v-card :title="editing ? 'Modifier la provision' : 'Nouvelle provision'">
      <v-card-text>
        <v-alert v-if="formError" type="error" variant="tonal" class="mb-4" :text="formError" />
        <v-form v-model="valid" @submit.prevent="save">
          <v-text-field v-model="label" label="Libellé" maxlength="100" :rules="[required]" autofocus />
          <v-autocomplete
            v-model="categoryId"
            :items="categoryItems"
            label="Catégorie de dépense"
            :rules="[required]"
          />
          <v-text-field
            v-model="amount"
            label="Montant"
            suffix="€"
            inputmode="decimal"
            :rules="[amountRule]"
          />
          <v-switch
            v-model="isAnnual"
            color="primary"
            label="Revient chaque année"
            hide-details
            class="mb-2"
          />
          <v-row dense>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="dueDate"
                :label="isAnnual ? 'Prochaine échéance' : 'Échéance'"
                type="date"
                :rules="[required, dueRule]"
              />
            </v-col>
            <v-col cols="12" sm="6">
              <v-text-field
                v-model="startDate"
                label="Mettre de côté à partir du"
                type="date"
                :rules="[required]"
              />
            </v-col>
          </v-row>
          <p v-if="editing" class="text-caption text-medium-emphasis">
            Commencée le {{ formatDate(editing.startDate) }} · cycle de {{ editing.periods }} période(s).
          </p>
          <button type="submit" hidden />
        </v-form>
      </v-card-text>
      <v-card-actions>
        <template v-if="editing">
          <v-btn v-if="!confirmDelete" color="error" @click="confirmDelete = true">Supprimer</v-btn>
          <v-btn v-else color="error" variant="flat" :loading="deleting" @click="remove">
            Confirmer la suppression
          </v-btn>
        </template>
        <v-spacer />
        <v-btn @click="dialog = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save"
          >Enregistrer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
