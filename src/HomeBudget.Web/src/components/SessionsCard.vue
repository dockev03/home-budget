<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { formatDateTime } from '@/utils/users'
import { deviceIcon, lastSeenLabel, originLabel, sessionTitle } from '@/utils/sessions'

/** Sessions ouvertes de l'utilisateur (N8) : une session douteuse peut être fermée à distance. */
const sessions = ref<Schemas['SessionDto'][]>([])
const loading = ref(false)
const error = ref<string | null>(null)
const message = ref<string | null>(null)
const confirmId = ref<string | null>(null)
const confirmOthers = ref(false)

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

const load = () =>
  guarded(async () => {
    sessions.value = await unwrap(api.GET('/api/account/sessions'))
  })

const revoke = (id: string) =>
  guarded(async () => {
    await unwrap(api.DELETE('/api/account/sessions/{id}', { params: { path: { id } } }))
    sessions.value = sessions.value.filter((s) => s.id !== id)
    confirmId.value = null
    message.value = 'Session fermée : l’appareil devra se reconnecter.'
  })

const revokeOthers = () =>
  guarded(async () => {
    const { revoked } = await unwrap(api.POST('/api/account/sessions/revoke-others'))
    confirmOthers.value = false
    message.value = revoked === 0 ? 'Aucune autre session.' : `${revoked} session(s) fermée(s).`
    sessions.value = sessions.value.filter((s) => s.isCurrent)
  })

onMounted(load)
</script>

<template>
  <v-card :loading="loading && sessions.length === 0" data-testid="sessions">
    <v-card-title>Sessions actives</v-card-title>
    <v-card-subtitle class="text-wrap">
      Appareils connectés à votre compte. Fermez toute session que vous ne reconnaissez pas, puis changez
      votre mot de passe.
    </v-card-subtitle>
    <v-card-text v-if="error || message" class="pb-0">
      <v-alert v-if="error" type="error" variant="tonal" :text="error" closable @click:close="error = null" />
      <v-alert
        v-else-if="message"
        type="success"
        variant="tonal"
        :text="message"
        closable
        @click:close="message = null"
      />
    </v-card-text>
    <v-list lines="three">
      <v-list-item
        v-for="session in sessions"
        :key="session.id"
        :prepend-icon="deviceIcon(session.device)"
        data-testid="session"
      >
        <v-list-item-title class="d-flex align-center flex-wrap ga-2">
          {{ sessionTitle(session) }}
          <v-chip v-if="session.isCurrent" size="x-small" color="primary" variant="flat">Cet appareil</v-chip>
        </v-list-item-title>
        <v-list-item-subtitle>
          IP {{ session.ipAddress ?? 'inconnue' }} ·
          {{ session.isCurrent ? 'actif maintenant' : lastSeenLabel(session.lastSeenAt) }}
        </v-list-item-subtitle>
        <v-list-item-subtitle>
          Connecté le {{ formatDateTime(session.createdAt) }} ({{ originLabel(session.origin) }})
        </v-list-item-subtitle>
        <template v-if="!session.isCurrent" #append>
          <v-btn v-if="confirmId !== session.id" variant="text" color="error" @click="confirmId = session.id"
            >Révoquer</v-btn
          >
          <v-btn v-else variant="flat" color="error" :loading="loading" @click="revoke(session.id)"
            >Confirmer</v-btn
          >
        </template>
      </v-list-item>
    </v-list>
    <v-card-actions v-if="sessions.length > 1">
      <v-btn v-if="!confirmOthers" color="error" @click="confirmOthers = true"
        >Déconnecter les autres sessions</v-btn
      >
      <template v-else>
        <span class="text-body-2 mx-2">Fermer les {{ sessions.length - 1 }} autre(s) session(s) ?</span>
        <v-btn @click="confirmOthers = false">Annuler</v-btn>
        <v-btn color="error" variant="flat" :loading="loading" @click="revokeOthers">Confirmer</v-btn>
      </template>
    </v-card-actions>
  </v-card>
</template>
