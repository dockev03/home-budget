<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useDisplay } from 'vuetify'
import { mdiClose } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { dateFormats, decimalSeparators, delimiters } from '@/utils/imports'
import { required } from '@/utils/rules'

type Profile = Schemas['ImportProfileDto']

const props = defineProps<{ profile: Profile | null }>()
const visible = defineModel<boolean>({ required: true })
const emit = defineEmits<{ saved: [profile: Profile | null] }>()

const { smAndDown } = useDisplay()

const name = ref('')
const delimiter = ref(';')
const dateFormat = ref('dd/MM/yyyy')
const decimalSeparator = ref(',')
const dateColumn = ref('1')
const labelColumn = ref('2')
const amountMode = ref<'single' | 'split'>('single')
const amountColumn = ref('3')
const debitColumn = ref('3')
const creditColumn = ref('4')

const valid = ref(false)
const saving = ref(false)
const deleting = ref(false)
const confirmDelete = ref(false)
const error = ref<string | null>(null)

const isEdit = computed(() => !!props.profile)
const columnRule = (v: string) => (/^\d{1,2}$/.test(v) && Number(v) >= 1 && Number(v) <= 50) || 'De 1 à 50.'

watch(visible, (open) => {
  if (!open) return
  error.value = null
  confirmDelete.value = false
  const p = props.profile
  name.value = p?.name ?? ''
  delimiter.value = p?.delimiter ?? ';'
  dateFormat.value = p?.dateFormat ?? 'dd/MM/yyyy'
  decimalSeparator.value = p?.decimalSeparator ?? ','
  dateColumn.value = String(p?.dateColumn ?? 1)
  labelColumn.value = String(p?.labelColumn ?? 2)
  amountMode.value = p && p.amountColumn === null ? 'split' : 'single'
  amountColumn.value = String(p?.amountColumn ?? 3)
  debitColumn.value = String(p?.debitColumn ?? 3)
  creditColumn.value = String(p?.creditColumn ?? 4)
})

async function save() {
  if (!valid.value) return
  saving.value = true
  error.value = null
  const split = amountMode.value === 'split'
  const body = {
    name: name.value.trim(),
    delimiter: delimiter.value,
    dateFormat: dateFormat.value,
    decimalSeparator: decimalSeparator.value,
    dateColumn: Number(dateColumn.value),
    labelColumn: Number(labelColumn.value),
    amountColumn: split ? null : Number(amountColumn.value),
    debitColumn: split ? Number(debitColumn.value) : null,
    creditColumn: split ? Number(creditColumn.value) : null,
  }
  try {
    const saved = props.profile
      ? await unwrap(
          api.PUT('/api/import-profiles/{id}', {
            params: { path: { id: props.profile.id } },
            headers: ifMatch(props.profile.version),
            body,
          }),
        )
      : await unwrap(api.POST('/api/import-profiles', { body }))
    visible.value = false
    emit('saved', saved as Profile)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove() {
  const p = props.profile
  if (!p) return
  deleting.value = true
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/import-profiles/{id}', {
        params: { path: { id: p.id } },
        headers: ifMatch(p.version),
      }),
    )
    visible.value = false
    emit('saved', null)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    deleting.value = false
  }
}
</script>

<template>
  <v-dialog v-model="visible" :fullscreen="smAndDown" max-width="560" scrollable>
    <v-card>
      <v-toolbar density="comfortable" color="surface">
        <v-btn :icon="mdiClose" aria-label="Fermer" @click="visible = false" />
        <v-toolbar-title>{{ isEdit ? 'Modifier le profil' : 'Nouveau profil CSV' }}</v-toolbar-title>
      </v-toolbar>

      <v-card-text>
        <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
        <v-form v-model="valid" @submit.prevent="save">
          <v-text-field v-model="name" label="Nom du profil" maxlength="60" :rules="[required]" v-autofocus />
          <v-row dense>
            <v-col cols="12" sm="6">
              <v-select v-model="delimiter" :items="delimiters" label="Séparateur de colonnes" />
            </v-col>
            <v-col cols="12" sm="6">
              <v-select v-model="decimalSeparator" :items="decimalSeparators" label="Séparateur décimal" />
            </v-col>
          </v-row>
          <v-select v-model="dateFormat" :items="dateFormats" label="Format des dates" />

          <p class="text-subtitle-2 mb-2">Numéro des colonnes (la première est la colonne 1)</p>
          <v-row dense>
            <v-col cols="6">
              <v-text-field v-model="dateColumn" label="Date" inputmode="numeric" :rules="[columnRule]" />
            </v-col>
            <v-col cols="6">
              <v-text-field v-model="labelColumn" label="Libellé" inputmode="numeric" :rules="[columnRule]" />
            </v-col>
          </v-row>
          <v-radio-group v-model="amountMode" inline hide-details class="mb-2">
            <v-radio value="single" label="Montant signé" />
            <v-radio value="split" label="Débit et crédit séparés" />
          </v-radio-group>
          <v-row dense>
            <v-col v-if="amountMode === 'single'" cols="6">
              <v-text-field
                v-model="amountColumn"
                label="Montant"
                inputmode="numeric"
                :rules="[columnRule]"
              />
            </v-col>
            <template v-else>
              <v-col cols="6">
                <v-text-field v-model="debitColumn" label="Débit" inputmode="numeric" :rules="[columnRule]" />
              </v-col>
              <v-col cols="6">
                <v-text-field
                  v-model="creditColumn"
                  label="Crédit"
                  inputmode="numeric"
                  :rules="[columnRule]"
                />
              </v-col>
            </template>
          </v-row>
          <p class="text-caption text-medium-emphasis">
            Les lignes situées avant la première date lisible (en-tête du relevé, titres des colonnes) sont
            ignorées. L’encodage (UTF-8 ou Windows-1252) est détecté.
          </p>
          <button type="submit" hidden />
        </v-form>
      </v-card-text>

      <v-card-actions>
        <template v-if="isEdit">
          <v-btn v-if="!confirmDelete" color="error" @click="confirmDelete = true">Supprimer</v-btn>
          <v-btn v-else color="error" variant="flat" :loading="deleting" @click="remove"
            >Confirmer la suppression</v-btn
          >
        </template>
        <v-spacer />
        <v-btn @click="visible = false">Annuler</v-btn>
        <v-btn color="primary" variant="flat" :loading="saving" :disabled="!valid" @click="save"
          >Enregistrer</v-btn
        >
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>
