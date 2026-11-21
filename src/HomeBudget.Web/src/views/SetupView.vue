<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { mdiEye, mdiEyeOff } from '@mdi/js'
import { useAuthStore } from '@/stores/auth'
import { ApiError } from '@/api/errors'
import { passwordRules, required } from '@/utils/rules'

const auth = useAuthStore()
const router = useRouter()

const token = ref('')
const userName = ref('')
const email = ref('')
const password = ref('')
const confirmation = ref('')
const showPassword = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)
const valid = ref(false)

const sameAsPassword = (v: string) => v === password.value || 'Les mots de passe ne correspondent pas.'

async function submit() {
  if (!valid.value) return
  loading.value = true
  error.value = null
  try {
    await auth.setup({
      token: token.value.trim(),
      userName: userName.value.trim(),
      email: email.value.trim() || null,
      password: password.value,
    })
    await router.replace({ name: 'security' })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Création impossible.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <v-card class="pa-2">
    <v-card-title class="text-h6">Premier lancement</v-card-title>
    <v-card-text>
      <p class="mb-4 text-medium-emphasis">
        Créez le compte administrateur. Le <strong>jeton de premier lancement</strong> est affiché dans les
        logs du conteneur (Portainer → conteneur <code>home-budget</code> → Logs).
      </p>

      <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

      <v-form v-model="valid" @submit.prevent="submit">
        <v-text-field
          v-model="token"
          label="Jeton de premier lancement"
          :rules="[required]"
          autocomplete="off"
        />
        <v-text-field v-model="userName" label="Identifiant" :rules="[required]" autocomplete="username" />
        <v-text-field v-model="email" label="E-mail (facultatif)" type="email" autocomplete="email" />
        <v-text-field
          v-model="password"
          label="Mot de passe"
          :type="showPassword ? 'text' : 'password'"
          :append-inner-icon="showPassword ? mdiEyeOff : mdiEye"
          :rules="passwordRules"
          hint="12 caractères minimum. Une phrase de passe est idéale."
          persistent-hint
          autocomplete="new-password"
          @click:append-inner="showPassword = !showPassword"
        />
        <v-text-field
          v-model="confirmation"
          class="mt-2"
          label="Confirmation du mot de passe"
          :type="showPassword ? 'text' : 'password'"
          :rules="[required, sameAsPassword]"
          autocomplete="new-password"
        />
        <v-btn type="submit" color="primary" block size="large" :loading="loading" :disabled="!valid">
          Créer le compte administrateur
        </v-btn>
      </v-form>
    </v-card-text>
  </v-card>
</template>
