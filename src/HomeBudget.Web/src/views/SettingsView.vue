<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { mdiDownload, mdiFileUploadOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { usePendingStore } from '@/stores/pending'
import { useReferenceStore } from '@/stores/reference'
import { isRestoreConfirmed, RESTORE_CONFIRMATION } from '@/utils/exports'

const reference = useReferenceStore()
const pending = usePendingStore()
const restoreFile = ref<File | File[] | null>(null)
const restoreConfirmation = ref('')
const restoring = ref(false)
const restored = ref<Schemas['RestoreResultDto'] | null>(null)
const restoreError = ref<string | null>(null)
const archiveFile = ref<File | File[] | null>(null)
const importingArchive = ref(false)
const archiveResult = ref<Schemas['AttachmentArchiveResultDto'] | null>(null)
const archiveError = ref<string | null>(null)

const settings = ref<Schemas['SettingsDto'] | null>(null)
const form = ref<Schemas['SettingsRequest']>({
  periodMode: 'Calendar',
  periodStartDay: 1,
  budgetWarningPercent: 80,
  budgetAlertPercent: 100,
  forecastStrategy: 'Budget',
  forecastAverageMonths: 6,
  emergencyFundAverageMonths: 6,
})
const valid = ref(false)
const saving = ref(false)
const saved = ref(false)
const error = ref<string | null>(null)

const startDays = Array.from({ length: 28 }, (_, i) => i + 1)
const averageMonths = Array.from({ length: 24 }, (_, i) => ({
  value: i + 1,
  title: i === 0 ? '1 mois' : `${i + 1} mois`,
}))
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
    const {
      periodMode,
      periodStartDay,
      budgetWarningPercent,
      budgetAlertPercent,
      forecastStrategy,
      forecastAverageMonths,
      emergencyFundAverageMonths,
    } = settings.value
    form.value = {
      periodMode,
      periodStartDay,
      budgetWarningPercent,
      budgetAlertPercent,
      forecastStrategy,
      forecastAverageMonths,
      emergencyFundAverageMonths,
    }
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
          forecastAverageMonths: Number(form.value.forecastAverageMonths),
          emergencyFundAverageMonths: Number(form.value.emergencyFundAverageMonths),
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

async function restore() {
  const file = Array.isArray(restoreFile.value) ? restoreFile.value[0] : restoreFile.value
  if (!file || !isRestoreConfirmed(restoreConfirmation.value)) return
  restoring.value = true
  restored.value = null
  restoreError.value = null
  try {
    // Le fichier est envoyé tel quel : le serveur le valide en entier avant de toucher à la base.
    restored.value = await unwrap(
      api.POST('/api/backups/restore', {
        body: file as unknown as Schemas['BackupDocument'],
        bodySerializer: (body) => body as unknown as BodyInit,
        headers: { 'Content-Type': 'application/json' },
      }),
    )
    restoreFile.value = null
    restoreConfirmation.value = ''
    await Promise.all([load(), reference.load(true), pending.load()])
  } catch (e) {
    restoreError.value = e instanceof ApiError ? e.message : 'Restauration impossible.'
  } finally {
    restoring.value = false
  }
}

/** H6, B7 : ajoute les fichiers de pièces jointes manquants (après une restauration sur une autre instance). */
async function importArchive() {
  const file = Array.isArray(archiveFile.value) ? archiveFile.value[0] : archiveFile.value
  if (!file) return
  importingArchive.value = true
  archiveResult.value = null
  archiveError.value = null
  try {
    archiveResult.value = await unwrap(
      api.POST('/api/backups/attachments', {
        body: file as unknown as string,
        bodySerializer: (body) => body as unknown as BodyInit,
        headers: { 'Content-Type': 'application/zip' },
      }),
    )
    archiveFile.value = null
  } catch (e) {
    archiveError.value = e instanceof ApiError ? e.message : 'Import impossible.'
  } finally {
    importingArchive.value = false
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

    <v-card class="mb-4" title="Prévision">
      <v-card-text>
        <v-radio-group
          v-model="form.forecastStrategy"
          hide-details
          class="mb-2"
          data-testid="forecast-strategy"
        >
          <v-radio value="Budget" label="Par le budget : le reste du budget de chaque catégorie est prévu" />
          <v-radio
            value="Average"
            label="Par la moyenne : le réalisé moyen des derniers mois remplace le budget"
          />
        </v-radio-group>
        <v-select
          v-if="form.forecastStrategy === 'Average'"
          v-model="form.forecastAverageMonths"
          :items="averageMonths"
          label="Moyenne sur"
          hint="Périodes clôturées qui précèdent la période en cours. Les échéances des récurrences restent prévues telles quelles."
          persistent-hint
          style="max-width: 420px"
        />
      </v-card-text>
    </v-card>

    <v-card class="mb-4" title="Épargne de précaution">
      <v-card-text>
        <v-select
          v-model="form.emergencyFundAverageMonths"
          :items="averageMonths"
          label="Dépenses moyennes sur"
          hint="Périodes clôturées retenues pour estimer combien de mois de dépenses l'épargne de précaution couvre."
          persistent-hint
          style="max-width: 420px"
          data-testid="emergency-fund-months"
        />
      </v-card-text>
    </v-card>

    <v-btn type="submit" color="primary" :loading="saving" :disabled="!valid">Enregistrer</v-btn>
  </v-form>

  <!-- H6 : sauvegarde et restauration complètes au format JSON. -->
  <v-card class="mt-8" title="Sauvegarde et restauration" data-testid="backup">
    <v-card-text>
      <p class="mb-4">
        Le fichier JSON contient les paramètres, comptes et leurs valorisations, catégories, tiers, budgets,
        opérations, récurrences, échéances, scénarios, objectifs d'épargne et la liste des pièces jointes. Il
        ne contient ni les fichiers joints (archive à part, ci-dessous), ni les comptes utilisateurs, ni les
        mots de passe, ni les secrets de double authentification. Conservez-le en lieu sûr : il contient
        toutes vos données financières.
      </p>
      <v-btn href="/api/backups/export" download color="primary" variant="tonal" :prepend-icon="mdiDownload"
        >Télécharger la sauvegarde</v-btn
      >
    </v-card-text>
    <v-divider />
    <v-card-text>
      <v-alert type="warning" variant="tonal" density="compact" class="mb-4">
        La restauration <strong>remplace toutes les données du foyer</strong> par celles du fichier. Les
        utilisateurs sont conservés, et un instantané de la base est pris juste avant.
      </v-alert>
      <v-alert v-if="restoreError" type="error" variant="tonal" class="mb-4" :text="restoreError" />
      <v-alert v-if="restored" type="success" variant="tonal" class="mb-4" closable>
        Données restaurées : {{ restored.accounts }} comptes, {{ restored.transactions }} opérations,
        {{ restored.recurrences }} récurrences. Instantané préalable : {{ restored.snapshot }}.
        <template v-if="(restored.missingAttachmentFiles ?? 0) > 0">
          <br />{{ restored.missingAttachmentFiles }} fichier(s) de pièces jointes manquent : importez
          l'archive des pièces jointes ci-dessous.
        </template>
      </v-alert>
      <v-file-input
        v-model="restoreFile"
        label="Fichier de sauvegarde (.json)"
        accept="application/json,.json"
        :prepend-icon="mdiFileUploadOutline"
        style="max-width: 520px"
      />
      <v-text-field
        v-model="restoreConfirmation"
        :label="`Saisissez ${RESTORE_CONFIRMATION} pour confirmer`"
        autocomplete="off"
        style="max-width: 520px"
      />
      <v-btn
        color="error"
        :loading="restoring"
        :disabled="!restoreFile || !isRestoreConfirmed(restoreConfirmation)"
        @click="restore"
        >Restaurer</v-btn
      >
    </v-card-text>
    <v-divider />
    <!-- B7 : fichiers des pièces jointes, nommés par empreinte ; l'import n'ajoute que les manquants. -->
    <v-card-text data-testid="attachments-archive">
      <p class="text-subtitle-2 mb-2">Pièces jointes</p>
      <p class="mb-4">
        L'archive ZIP contient les fichiers joints aux opérations. Après une restauration sur une autre
        instance, importez-la pour retrouver les fichiers : seuls les fichiers manquants et intacts sont
        ajoutés.
      </p>
      <v-btn href="/api/backups/attachments" download variant="tonal" :prepend-icon="mdiDownload" class="mb-4"
        >Télécharger l'archive des pièces jointes</v-btn
      >
      <v-alert v-if="archiveError" type="error" variant="tonal" class="mb-4" :text="archiveError" />
      <v-alert v-if="archiveResult" type="success" variant="tonal" class="mb-4" closable>
        {{ archiveResult.imported }} fichier(s) ajouté(s), {{ archiveResult.skipped }} déjà présent(s),
        {{ archiveResult.rejected }} refusé(s). Fichiers encore manquants : {{ archiveResult.missing }}.
      </v-alert>
      <v-file-input
        v-model="archiveFile"
        label="Archive des pièces jointes (.zip)"
        accept="application/zip,.zip"
        :prepend-icon="mdiFileUploadOutline"
        style="max-width: 520px"
      />
      <v-btn :loading="importingArchive" :disabled="!archiveFile" @click="importArchive"
        >Importer l'archive</v-btn
      >
    </v-card-text>
  </v-card>
</template>
