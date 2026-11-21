<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'

const settings = ref<Schemas['SettingsDto'] | null>(null)
const form = ref<Schemas['SettingsRequest']>({
  periodMode: 'Calendar',
  periodStartDay: 1,
  budgetWarningPercent: 80,
  budgetAlertPercent: 100,
})
const valid = ref(false)
const saving = ref(false)
const saved = ref(false)
const error = ref<string | null>(null)

const startDays = Array.from({ length: 28 }, (_, i) => i + 1)
const percentRule = (max: number) => (v: unknown) => {
  const n = Number(v)
  return (Number.isInteger(n) && n >= 1 && n <= max) || `Nombre entier de 1 à ${max}.`
}
const orderRule = () =>
  Number(form.value.budgetWarningPercent) < Number(form.value.budgetAlertPercent) ||
  "L'avertissement doit précéder l'alerte."

async function load() {
  try {
    settings.value = await unwrap(api.GET('/api/settings'))
    const { periodMode, periodStartDay, budgetWarningPercent, budgetAlertPercent } = settings.value
    form.value = { periodMode, periodStartDay, budgetWarningPercent, budgetAlertPercent }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

async function save() {
  if (!settings.value || !valid.value) return
  saving.value = true
  saved.value = false
  error.value = null
  try {
    settings.value = await unwrap(
      api.PUT('/api/settings', {
        headers: ifMatch(settings.value.version),
        body: {
          ...form.value,
          budgetWarningPercent: Number(form.value.budgetWarningPercent),
          budgetAlertPercent: Number(form.value.budgetAlertPercent),
        },
      }),
    )
    saved.value = true
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Paramètres du foyer</h1>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
  <v-alert v-if="saved" type="success" variant="tonal" class="mb-4" text="Paramètres enregistrés." closable />

  <v-form v-if="settings" v-model="valid" @submit.prevent="save">
    <v-card class="mb-4" title="Période budgétaire">
      <v-card-text>
        <v-radio-group v-model="form.periodMode" hide-details class="mb-2">
          <v-radio value="Calendar" label="Calendaire : du 1er au dernier jour du mois" />
          <v-radio value="FixedDay" label="Jour fixe : du jour N à la veille du N suivant (ex. paie le 25)" />
        </v-radio-group>
        <v-select
          v-if="form.periodMode === 'FixedDay'"
          v-model="form.periodStartDay"
          :items="startDays"
          label="Jour de début"
          hint="Jusqu'au 15, la période porte le nom du mois où elle commence ; à partir du 16, celui du mois où elle se termine."
          persistent-hint
          style="max-width: 420px"
        />
      </v-card-text>
    </v-card>

    <v-card class="mb-4" title="Jauges du budget">
      <v-card-text class="d-flex flex-wrap ga-4">
        <v-text-field
          v-model.number="form.budgetWarningPercent"
          label="Avertissement"
          suffix="%"
          type="number"
          :rules="[percentRule(100), orderRule]"
          style="max-width: 200px"
        />
        <v-text-field
          v-model.number="form.budgetAlertPercent"
          label="Alerte (dépassement)"
          suffix="%"
          type="number"
          :rules="[percentRule(200)]"
          style="max-width: 200px"
        />
      </v-card-text>
    </v-card>

    <v-btn type="submit" color="primary" :loading="saving" :disabled="!valid">Enregistrer</v-btn>
  </v-form>
</template>
