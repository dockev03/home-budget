<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { mdiEye, mdiEyeOff } from '@mdi/js'
import { useAuthStore, type LoginOutcome } from '@/stores/auth'
import { ApiError } from '@/api/errors'
import { required, totpCode } from '@/utils/rules'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const step = ref<'password' | 'totp' | 'recovery'>('password')
const userName = ref('')
const password = ref('')
const rememberMe = ref(false)
const code = ref('')
const recoveryCode = ref('')
const showPassword = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)
const valid = ref(false)

async function run(action: () => Promise<LoginOutcome>) {
  if (!valid.value) return
  loading.value = true
  error.value = null
  try {
    const outcome = await action()
    if (outcome === 'twoFactorRequired') {
      step.value = 'totp'
      return
    }
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/')
    await router.replace(redirect ? (route.query.redirect as string) : { name: 'home' })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Connexion impossible.'
  } finally {
    loading.value = false
  }
}

const submitPassword = () => run(() => auth.login(userName.value.trim(), password.value, rememberMe.value))
const submitCode = () => run(() => auth.loginTwoFactor(code.value.replace(/\s/g, ''), rememberMe.value))
const submitRecovery = () => run(() => auth.loginRecoveryCode(recoveryCode.value.trim()))

function backToPassword() {
  step.value = 'password'
  password.value = ''
  code.value = ''
  recoveryCode.value = ''
  error.value = null
}
</script>

<template>
  <v-card class="pa-2">
    <v-card-title class="text-h6">
      {{ step === 'password' ? 'Connexion' : 'Double authentification' }}
    </v-card-title>
    <v-card-text>
      <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

      <v-form v-if="step === 'password'" v-model="valid" @submit.prevent="submitPassword">
        <v-text-field
          v-model="userName"
          label="Identifiant"
          :rules="[required]"
          autocomplete="username"
          autofocus
        />
        <v-text-field
          v-model="password"
          label="Mot de passe"
          :type="showPassword ? 'text' : 'password'"
          :append-inner-icon="showPassword ? mdiEyeOff : mdiEye"
          :rules="[required]"
          autocomplete="current-password"
          @click:append-inner="showPassword = !showPassword"
        />
        <v-checkbox
          v-model="rememberMe"
          label="Rester connecté sur cet appareil"
          density="compact"
          hide-details
        />
        <v-btn
          type="submit"
          color="primary"
          block
          size="large"
          class="mt-4"
          :loading="loading"
          :disabled="!valid"
        >
          Se connecter
        </v-btn>
      </v-form>

      <v-form v-else-if="step === 'totp'" v-model="valid" @submit.prevent="submitCode">
        <p class="mb-4 text-medium-emphasis">
          Saisissez le code à 6 chiffres de votre application d'authentification.
        </p>
        <v-text-field
          v-model="code"
          label="Code"
          inputmode="numeric"
          autocomplete="one-time-code"
          maxlength="7"
          :rules="[required, totpCode]"
          autofocus
        />
        <v-btn type="submit" color="primary" block size="large" :loading="loading" :disabled="!valid">
          Valider
        </v-btn>
        <div class="d-flex justify-space-between mt-4">
          <v-btn variant="text" size="small" @click="backToPassword">Retour</v-btn>
          <v-btn variant="text" size="small" @click="step = 'recovery'">Utiliser un code de secours</v-btn>
        </div>
      </v-form>

      <v-form v-else v-model="valid" @submit.prevent="submitRecovery">
        <p class="mb-4 text-medium-emphasis">
          Saisissez l'un de vos codes de secours (format XXXXX-XXXXX). Chaque code ne fonctionne qu'une fois.
        </p>
        <v-text-field
          v-model="recoveryCode"
          label="Code de secours"
          :rules="[required]"
          autocomplete="off"
          autofocus
        />
        <v-btn type="submit" color="primary" block size="large" :loading="loading" :disabled="!valid">
          Valider
        </v-btn>
        <div class="d-flex justify-space-between mt-4">
          <v-btn variant="text" size="small" @click="backToPassword">Retour</v-btn>
          <v-btn variant="text" size="small" @click="step = 'totp'">Utiliser l'application</v-btn>
        </div>
      </v-form>
    </v-card-text>
  </v-card>
</template>
