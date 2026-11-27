<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiEmailOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'

/** Alertes de sécurité par e-mail (N9) : à chaque connexion, et quand le compte est verrouillé. */
const settings = ref<Schemas['SecurityAlertSettings'] | null>(null)
const saving = ref(false)
const error = ref<string | null>(null)

const disabledReason = computed(() => {
  if (!settings.value) return null
  if (!settings.value.emailAvailable)
    return 'L’envoi d’e-mails n’est pas configuré sur le serveur (HB__Smtp__*).'
  if (!settings.value.email) return 'Enregistrez d’abord votre adresse e-mail dans Notifications.'
  return null
})

async function load() {
  try {
    settings.value = await unwrap(api.GET('/api/account/security-alerts'))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

async function save(change: Partial<Schemas['SecurityAlertRequest']>) {
  if (!settings.value) return
  saving.value = true
  error.value = null
  const body = { onLogin: settings.value.onLogin, onLockout: settings.value.onLockout, ...change }
  try {
    settings.value = await unwrap(api.PUT('/api/account/security-alerts', { body }))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <v-card :loading="!settings && !error" data-testid="security-alerts">
    <v-card-title class="d-flex align-center ga-2">
      <v-icon :icon="mdiEmailOutline" />
      Alertes par e-mail
    </v-card-title>
    <v-card-text>
      <v-alert v-if="error" type="error" variant="tonal" class="mb-3" :text="error" closable />
      <v-alert v-if="disabledReason" type="info" variant="tonal" class="mb-3" :text="disabledReason" />
      <p v-else-if="settings" class="text-body-2 mb-2">
        Envoyées à <strong>{{ settings.email }}</strong> avec la date, l’adresse IP, le navigateur et le
        système.
      </p>
      <template v-if="settings">
        <v-switch
          :model-value="settings.onLogin"
          label="À chaque connexion à mon compte"
          color="primary"
          hide-details
          :disabled="!!disabledReason"
          :loading="saving"
          data-testid="alert-login"
          @update:model-value="save({ onLogin: !!$event })"
        />
        <v-switch
          :model-value="settings.onLockout"
          label="Quand mon compte est verrouillé après plusieurs échecs"
          color="primary"
          hide-details
          :disabled="!!disabledReason"
          :loading="saving"
          data-testid="alert-lockout"
          @update:model-value="save({ onLockout: !!$event })"
        />
      </template>
    </v-card-text>
  </v-card>
</template>
