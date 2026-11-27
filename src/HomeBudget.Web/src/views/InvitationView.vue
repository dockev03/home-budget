<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { useAuthStore } from '@/stores/auth'
import { passwordRules, required } from '@/utils/rules'
import { roleLabel, tokenFromHash } from '@/utils/users'

/** Acceptation d'une invitation (K1) : la personne invitée choisit son mot de passe. */
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

const token = tokenFromHash(route.hash)
const invitation = ref<Schemas['InvitationPreview'] | null>(null)
const password = ref('')
const confirmation = ref('')
const valid = ref(false)
const loading = ref(true)
const saving = ref(false)
const error = ref<string | null>(null)

const sameAsPassword = (value: string) => value === password.value || 'Les deux saisies diffèrent.'

onMounted(async () => {
  try {
    if (!token) throw new ApiError('Lien d’invitation incomplet.', 400)
    invitation.value = await unwrap(api.POST('/api/invitations/lookup', { body: { token } }))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Invitation illisible.'
  } finally {
    loading.value = false
  }
})

async function accept() {
  if (!valid.value || !token) return
  saving.value = true
  error.value = null
  try {
    await auth.acceptInvitation(token, password.value)
    // Le lien ne sert plus : il est retiré de l'historique du navigateur.
    await router.replace({ name: 'security' })
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Création du compte impossible.'
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <v-card class="pa-2" :loading="loading">
    <v-card-title class="text-h6">Invitation</v-card-title>
    <v-card-text>
      <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
      <template v-if="invitation">
        <p class="mb-4">
          Bienvenue <strong>{{ invitation.displayName || invitation.userName }}</strong> ! Votre identifiant
          est <strong>{{ invitation.userName }}</strong> ({{ roleLabel(invitation.role).toLowerCase() }}).
          Choisissez votre mot de passe ; vous activerez ensuite la double authentification.
        </p>
        <v-form v-model="valid" @submit.prevent="accept">
          <input type="text" name="username" :value="invitation.userName" autocomplete="username" hidden />
          <v-text-field
            v-model="password"
            label="Mot de passe"
            type="password"
            autocomplete="new-password"
            hint="12 caractères minimum ; une phrase de passe est idéale."
            :rules="passwordRules"
            v-autofocus
          />
          <v-text-field
            v-model="confirmation"
            label="Confirmation"
            type="password"
            autocomplete="new-password"
            :rules="[required, sameAsPassword]"
          />
          <v-btn type="submit" color="primary" block :loading="saving" :disabled="!valid"
            >Créer mon compte</v-btn
          >
        </v-form>
      </template>
      <v-btn v-else-if="!loading" variant="text" :to="{ name: 'login' }">Aller à la connexion</v-btn>
    </v-card-text>
  </v-card>
</template>
