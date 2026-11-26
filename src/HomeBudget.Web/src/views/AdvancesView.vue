<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiPlus } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { formatAmount, formatDate, parseAmount, todayIso } from '@/utils/format'
import { advanceProgress } from '@/utils/reconciliation'
import { required } from '@/utils/rules'

type Advance = Schemas['AdvanceDto']

/** Avances à recevoir (B12) : notes de frais, avance pro, CSE… */
const auth = useAuthStore()
const { smAndDown } = useDisplay()

const advances = ref<Advance[]>([])
const error = ref<string | null>(null)

const dialog = ref(false)
const editing = ref<Advance | null>(null)
const form = ref({ label: '', amount: '', date: todayIso(), notes: '', isClosed: false })
const valid = ref(false)
const saving = ref(false)
const deleting = ref(false)
const confirmDelete = ref(false)
const dialogError = ref<string | null>(null)

const positiveRule = (v: string) =>
  (parseAmount(v) ?? 0) > 0 || 'Montant positif attendu (2 décimales au plus).'

async function load() {
  try {
    advances.value = await unwrap(api.GET('/api/advances'))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

function open(advance: Advance | null) {
  editing.value = advance
  form.value = advance
    ? {
        label: advance.label,
        amount: String(advance.expectedAmount).replace('.', ','),
        date: advance.date,
        notes: advance.notes ?? '',
        isClosed: advance.isClosed,
      }
    : { label: '', amount: '', date: todayIso(), notes: '', isClosed: false }
  dialogError.value = null
  confirmDelete.value = false
  dialog.value = true
}

async function save() {
  if (!valid.value) return
  saving.value = true
  dialogError.value = null
  const body = {
    label: form.value.label.trim(),
    expectedAmount: parseAmount(form.value.amount)!,
    date: form.value.date,
    notes: form.value.notes.trim() || null,
    isClosed: form.value.isClosed,
  }
  try {
    if (editing.value) {
      await unwrap(
        api.PUT('/api/advances/{id}', {
          params: { path: { id: editing.value.id } },
          headers: ifMatch(editing.value.version),
          body,
        }),
      )
    } else {
      await unwrap(api.POST('/api/advances', { body }))
    }
    dialog.value = false
    await load()
  } catch (e) {
    dialogError.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove() {
  if (!editing.value) return
  if (!confirmDelete.value) {
    confirmDelete.value = true
    return
  }
  deleting.value = true
  try {
    await unwrap(
      api.DELETE('/api/advances/{id}', {
        params: { path: { id: editing.value.id } },
        headers: ifMatch(editing.value.version),
      }),
    )
    dialog.value = false
    await load()
  } catch (e) {
    dialogError.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="d-flex align-center mb-4">
    <h1 class="text-h5 font-weight-bold mr-auto">Avances à recevoir</h1>
    <v-btn v-if="auth.canWrite" color="primary" :prepend-icon="mdiPlus" @click="open(null)"
      >Nouvelle avance</v-btn
    >
  </div>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
  <p class="text-body-2 text-medium-emphasis mb-4">
    Notes de frais, avance professionnelle, participation du CSE… Rattachez les remboursements reçus depuis le
    formulaire d’un revenu : l’avance est soldée quand ils atteignent le montant attendu.
  </p>

  <v-card v-if="!advances.length" class="pa-6 text-center text-medium-emphasis">Aucune avance.</v-card>
  <v-card v-else>
    <v-list lines="two">
      <v-list-item
        v-for="a in advances"
        :key="a.id"
        :data-testid="`advance-${a.id}`"
        @click="auth.canWrite && open(a)"
      >
        <v-list-item-title class="d-flex align-center ga-2">
          {{ a.label }}
          <v-chip :color="a.status === 'Open' ? 'warning' : 'success'" size="x-small">
            {{ a.status === 'Open' ? 'Ouverte' : 'Soldée' }}
          </v-chip>
        </v-list-item-title>
        <v-list-item-subtitle>
          {{ formatDate(a.date) }} · reçu {{ formatAmount(a.received) }} sur
          {{ formatAmount(a.expectedAmount) }}
          <template v-if="a.transactionCount"> ({{ a.transactionCount }} versement(s))</template>
        </v-list-item-subtitle>
        <v-progress-linear
          :model-value="advanceProgress(a)"
          :color="a.status === 'Open' ? 'warning' : 'success'"
          class="mt-2"
          rounded
        />
        <template #append>
          <span class="font-weight-medium text-no-wrap">
            {{ a.status === 'Open' ? `Reste ${formatAmount(a.remaining)}` : 'Soldée' }}
          </span>
        </template>
      </v-list-item>
    </v-list>
  </v-card>

  <v-dialog v-model="dialog" :fullscreen="smAndDown" max-width="520">
    <v-card :title="editing ? 'Modifier l’avance' : 'Nouvelle avance'">
      <v-card-text>
        <v-alert v-if="dialogError" type="error" variant="tonal" class="mb-4" :text="dialogError" />
        <v-form v-model="valid" @submit.prevent="save">
          <v-text-field v-model="form.label" label="Libellé" maxlength="100" :rules="[required]" />
          <v-row dense>
            <v-col cols="6">
              <v-text-field
                v-model="form.amount"
                label="Montant attendu"
                suffix="€"
                inputmode="decimal"
                :rules="[positiveRule]"
              />
            </v-col>
            <v-col cols="6">
              <v-text-field v-model="form.date" label="Date" type="date" :rules="[required]" />
            </v-col>
          </v-row>
          <v-textarea v-model="form.notes" label="Note" rows="2" auto-grow maxlength="2000" />
          <v-switch
            v-model="form.isClosed"
            label="Soldée (même si le reçu n’atteint pas le montant attendu)"
            color="success"
            hide-details
          />
          <button type="submit" hidden />
        </v-form>
      </v-card-text>
      <v-card-actions>
        <v-btn
          v-if="editing"
          color="error"
          :variant="confirmDelete ? 'flat' : 'text'"
          :loading="deleting"
          @click="remove"
          >{{ confirmDelete ? 'Confirmer la suppression' : 'Supprimer' }}</v-btn
        >
        <v-spacer />
        <v-btn @click="dialog = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save"
          >Enregistrer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
