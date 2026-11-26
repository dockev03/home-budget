<script setup lang="ts">
import { onMounted, ref } from 'vue'
import QRCode from 'qrcode'
import { mdiContentCopy, mdiDownload, mdiShieldCheck, mdiShieldOffOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { passwordRules, required, totpCode } from '@/utils/rules'

const auth = useAuthStore()

const status = ref<Schemas['TwoFactorStatus'] | null>(null)
const setup = ref<Schemas['AuthenticatorSetup'] | null>(null)
const qrCode = ref<string | null>(null)
const code = ref('')
const recoveryCodes = ref<string[]>([])
const error = ref<string | null>(null)
const loading = ref(false)
const valid = ref(false)

// Dialogue de confirmation par mot de passe (désactivation, nouveaux codes de secours).
const passwordDialog = ref<'disable' | 'regenerate' | null>(null)
const password = ref('')

async function guarded(action: () => Promise<void>) {
  loading.value = true
  error.value = null
  try {
    await action()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Opération impossible.'
  } finally {
    loading.value = false
  }
}

const loadStatus = () =>
  guarded(async () => {
    status.value = await unwrap(api.GET('/api/account/two-factor'))
  })

const startSetup = () =>
  guarded(async () => {
    setup.value = await unwrap(api.POST('/api/account/two-factor/setup'))
    qrCode.value = await QRCode.toDataURL(setup.value.authenticatorUri, { margin: 1, width: 220 })
  })

const enable = () =>
  guarded(async () => {
    if (!valid.value) return
    const result = await unwrap(
      api.POST('/api/account/two-factor/enable', { body: { code: code.value.replace(/\s/g, '') } }),
    )
    recoveryCodes.value = result.codes
    setup.value = null
    qrCode.value = null
    code.value = ''
    await Promise.all([loadStatus(), auth.refreshUser()])
  })

const confirmPassword = () =>
  guarded(async () => {
    const body = { password: password.value }
    if (passwordDialog.value === 'disable') {
      await unwrap(api.POST('/api/account/two-factor/disable', { body }))
      recoveryCodes.value = []
      await auth.refreshUser()
    } else {
      recoveryCodes.value = (await unwrap(api.POST('/api/account/two-factor/recovery-codes', { body }))).codes
    }
    passwordDialog.value = null
    password.value = ''
    await loadStatus()
  })

function copyCodes() {
  void navigator.clipboard?.writeText(recoveryCodes.value.join('\n'))
}

function downloadCodes() {
  const text = `Home Budget — codes de secours (${auth.user?.userName})\n\n${recoveryCodes.value.join('\n')}\n`
  const url = URL.createObjectURL(new Blob([text], { type: 'text/plain' }))
  const link = Object.assign(document.createElement('a'), {
    href: url,
    download: 'home-budget-codes-secours.txt',
  })
  link.click()
  URL.revokeObjectURL(url)
}

// Changement du mot de passe : les autres sessions sont fermées par le serveur.
const passwordForm = ref({ current: '', next: '', confirmation: '' })
const passwordValid = ref(false)
const passwordChanged = ref(false)
const sameAsNew = (value: string) => value === passwordForm.value.next || 'Les deux saisies diffèrent.'

const changePassword = () =>
  guarded(async () => {
    if (!passwordValid.value) return
    passwordChanged.value = false
    await unwrap(
      api.POST('/api/account/password', {
        body: { currentPassword: passwordForm.value.current, newPassword: passwordForm.value.next },
      }),
    )
    passwordForm.value = { current: '', next: '', confirmation: '' }
    passwordChanged.value = true
  })

onMounted(loadStatus)
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Sécurité</h1>

  <v-alert
    v-if="auth.user?.twoFactorRequired && !auth.user.twoFactorEnabled"
    type="warning"
    variant="tonal"
    class="mb-4"
    data-testid="two-factor-required"
    title="Double authentification requise"
    text="Activez-la pour accéder à l'application : c'est obligatoire pour les comptes invités."
  />

  <v-alert
    v-if="error"
    type="error"
    variant="tonal"
    class="mb-4"
    :text="error"
    closable
    @click:close="error = null"
  />

  <v-card v-if="recoveryCodes.length" class="mb-4" color="success" variant="tonal">
    <v-card-title>Codes de secours</v-card-title>
    <v-card-text>
      <p class="mb-3">
        Conservez ces codes en lieu sûr (gestionnaire de mots de passe). Chacun permet <strong>une</strong>
        connexion si vous perdez votre téléphone. Ils ne seront plus affichés.
      </p>
      <v-row dense>
        <v-col v-for="c in recoveryCodes" :key="c" cols="6" sm="4">
          <code class="text-body-1">{{ c }}</code>
        </v-col>
      </v-row>
    </v-card-text>
    <v-card-actions>
      <v-btn :prepend-icon="mdiContentCopy" @click="copyCodes">Copier</v-btn>
      <v-btn :prepend-icon="mdiDownload" @click="downloadCodes">Télécharger</v-btn>
      <v-spacer />
      <v-btn @click="recoveryCodes = []">J'ai noté mes codes</v-btn>
    </v-card-actions>
  </v-card>

  <v-card :loading="loading && !status">
    <v-card-title class="d-flex align-center ga-2">
      <v-icon
        :icon="status?.enabled ? mdiShieldCheck : mdiShieldOffOutline"
        :color="status?.enabled ? 'success' : 'warning'"
      />
      Double authentification
    </v-card-title>

    <v-card-text v-if="status?.enabled">
      Active. Il vous reste <strong>{{ status.recoveryCodesLeft }}</strong> code(s) de secours.
    </v-card-text>
    <v-card-actions v-if="status?.enabled">
      <v-btn @click="passwordDialog = 'regenerate'">Nouveaux codes de secours</v-btn>
      <v-btn v-if="!auth.user?.twoFactorRequired" color="error" @click="passwordDialog = 'disable'"
        >Désactiver</v-btn
      >
    </v-card-actions>

    <template v-else-if="status && !setup">
      <v-card-text>
        Ajoutez une seconde vérification à la connexion avec une application d'authentification (Aegis, 2FAS,
        Google Authenticator, Bitwarden…).
      </v-card-text>
      <v-card-actions>
        <v-btn color="primary" variant="flat" :loading="loading" @click="startSetup">Activer</v-btn>
      </v-card-actions>
    </template>

    <v-card-text v-else-if="setup">
      <ol class="ml-4 mb-4">
        <li>Scannez ce QR code avec votre application d'authentification.</li>
        <li>Saisissez le code à 6 chiffres affiché pour confirmer.</li>
      </ol>
      <div class="d-flex flex-column flex-sm-row align-center ga-4 mb-4">
        <img v-if="qrCode" :src="qrCode" alt="QR code de configuration" width="220" height="220" class="qr" />
        <div>
          <div class="text-caption text-medium-emphasis">Saisie manuelle de la clé :</div>
          <code class="text-body-1">{{ setup.sharedKey }}</code>
        </div>
      </div>
      <v-form v-model="valid" @submit.prevent="enable">
        <v-text-field
          v-model="code"
          label="Code de vérification"
          inputmode="numeric"
          autocomplete="one-time-code"
          maxlength="7"
          :rules="[required, totpCode]"
          style="max-width: 260px"
        />
        <v-btn type="submit" color="primary" :loading="loading" :disabled="!valid"
          >Confirmer l'activation</v-btn
        >
        <v-btn variant="text" class="ml-2" @click="setup = null">Annuler</v-btn>
      </v-form>
    </v-card-text>
  </v-card>

  <v-card class="mt-4">
    <v-card-title>Mot de passe</v-card-title>
    <v-card-text>
      <v-alert
        v-if="passwordChanged"
        type="success"
        variant="tonal"
        class="mb-4"
        text="Mot de passe changé. Vos autres appareils devront se reconnecter."
      />
      <v-form v-model="passwordValid" style="max-width: 420px" @submit.prevent="changePassword">
        <v-text-field
          v-model="passwordForm.current"
          label="Mot de passe actuel"
          type="password"
          autocomplete="current-password"
          :rules="[required]"
        />
        <v-text-field
          v-model="passwordForm.next"
          label="Nouveau mot de passe"
          type="password"
          autocomplete="new-password"
          hint="12 caractères minimum ; une phrase de passe est idéale."
          :rules="passwordRules"
        />
        <v-text-field
          v-model="passwordForm.confirmation"
          label="Confirmation"
          type="password"
          autocomplete="new-password"
          :rules="[required, sameAsNew]"
        />
        <v-btn type="submit" color="primary" :loading="loading" :disabled="!passwordValid"
          >Changer le mot de passe</v-btn
        >
      </v-form>
    </v-card-text>
  </v-card>

  <v-dialog
    :model-value="passwordDialog !== null"
    max-width="420"
    @update:model-value="passwordDialog = null"
  >
    <v-card>
      <v-card-title>Confirmez votre mot de passe</v-card-title>
      <v-card-text>
        <v-text-field
          v-model="password"
          label="Mot de passe"
          type="password"
          autocomplete="current-password"
          autofocus
          @keyup.enter="confirmPassword"
        />
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn @click="passwordDialog = null">Annuler</v-btn>
        <v-btn
          :color="passwordDialog === 'disable' ? 'error' : 'primary'"
          :loading="loading"
          :disabled="!password"
          @click="confirmPassword"
        >
          {{ passwordDialog === 'disable' ? 'Désactiver' : 'Générer' }}
        </v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.qr {
  background: #fff;
  border-radius: 8px;
  padding: 4px;
}
</style>
