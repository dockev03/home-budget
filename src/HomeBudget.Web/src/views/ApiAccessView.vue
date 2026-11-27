<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { mdiApi, mdiContentCopy, mdiKeyPlus } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import WebhooksCard from '@/components/WebhooksCard.vue'
import { useAuthStore } from '@/stores/auth'
import {
  curlExample,
  daysLeft,
  isExpired,
  scopeLabel,
  TOKEN_DURATIONS,
  type TokenScope,
} from '@/utils/integrations'
import { required } from '@/utils/rules'
import { formatDateTime } from '@/utils/users'

/** API publique (I5) : jetons d'accès personnels et, pour les administrateurs, webhooks. */
const auth = useAuthStore()

const tokens = ref<Schemas['ApiTokenDto'][]>([])
const error = ref<string | null>(null)
const creating = ref(false)
const valid = ref(false)
const form = ref<{ name: string; scope: TokenScope; expiresInDays: number }>({
  name: '',
  scope: 'Read',
  expiresInDays: 90,
})
/** Jeton tout juste créé : montré une seule fois. */
const created = ref<string | null>(null)
const copied = ref(false)
const confirmId = ref<string | null>(null)
const origin = window.location.origin

const scopes: { value: TokenScope; title: string }[] = [
  { value: 'Read', title: scopeLabel('Read') },
  { value: 'Write', title: scopeLabel('Write') },
]

async function load() {
  try {
    tokens.value = await unwrap(api.GET('/api/tokens'))
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

async function create() {
  creating.value = true
  error.value = null
  copied.value = false
  try {
    const result = await unwrap(api.POST('/api/tokens', { body: form.value }))
    created.value = result.token
    tokens.value.push(result.item)
    form.value = { name: '', scope: 'Read', expiresInDays: 90 }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Création impossible.'
  } finally {
    creating.value = false
  }
}

async function revoke(id: string) {
  error.value = null
  try {
    await unwrap(api.DELETE('/api/tokens/{id}', { params: { path: { id } } }))
    tokens.value = tokens.value.filter((t) => t.id !== id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Révocation impossible.'
  } finally {
    confirmId.value = null
  }
}

async function copy() {
  if (!created.value) return
  await navigator.clipboard.writeText(created.value)
  copied.value = true
}

onMounted(load)
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Accès API</h1>
  <p class="mb-4">
    L'API REST permet à un script, un tableur ou un outil de domotique de lire ou d'enregistrer vos données.
    Sa documentation détaille chaque requête.
    <v-btn href="/api/docs/" target="_blank" rel="noopener" variant="text" size="small" :prepend-icon="mdiApi"
      >Documentation de l'API</v-btn
    >
  </p>

  <v-card title="Jetons d'accès personnels" data-testid="tokens">
    <v-card-text>
      <p class="mb-4 text-medium-emphasis">
        Un jeton agit en votre nom, avec vos comptes visibles, mais jamais avec les droits d'administration.
        Envoyez-le dans l'en-tête <code>Authorization: Bearer …</code>. Révoquez-le dès qu'il ne sert plus.
      </p>
      <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />
      <v-alert
        v-if="created"
        type="success"
        variant="tonal"
        class="mb-4"
        closable
        @click:close="created = null"
      >
        <p class="mb-2">Copiez ce jeton maintenant : <strong>il ne sera plus affiché</strong>.</p>
        <div class="d-flex align-center flex-wrap ga-2">
          <code class="text-body-2" data-testid="new-token" style="word-break: break-all">{{ created }}</code>
          <v-btn :prepend-icon="mdiContentCopy" size="small" variant="tonal" @click="copy">{{
            copied ? 'Copié' : 'Copier'
          }}</v-btn>
        </div>
        <p class="mt-2 text-caption">
          Exemple : <code>{{ curlExample(origin, created) }}</code>
        </p>
      </v-alert>

      <v-form v-model="valid" class="d-flex flex-wrap ga-3 align-start" @submit.prevent="create">
        <v-text-field
          v-model="form.name"
          label="Nom du jeton"
          placeholder="Tableur, Home Assistant…"
          :rules="[required]"
          maxlength="60"
          style="min-width: 220px; flex: 2"
        />
        <v-select
          v-model="form.scope"
          :items="scopes"
          label="Droits"
          :disabled="!auth.canWrite"
          style="min-width: 180px; flex: 1"
        />
        <v-select
          v-model="form.expiresInDays"
          :items="TOKEN_DURATIONS"
          label="Valable"
          style="min-width: 140px; flex: 1"
        />
        <v-btn
          type="submit"
          color="primary"
          :prepend-icon="mdiKeyPlus"
          :loading="creating"
          :disabled="!valid"
          class="mt-2"
          >Créer</v-btn
        >
      </v-form>
    </v-card-text>
    <v-divider />
    <v-list v-if="tokens.length > 0" lines="two">
      <v-list-item v-for="token in tokens" :key="token.id" :title="token.name">
        <v-list-item-subtitle>
          <code>{{ token.prefix }}…</code> · {{ scopeLabel(token.scope) }} ·
          <span v-if="isExpired(token.expiresAt)" class="text-error">expiré</span>
          <span v-else>expire dans {{ daysLeft(token.expiresAt) }} j</span>
          · {{ token.lastUsedAt ? `utilisé le ${formatDateTime(token.lastUsedAt)}` : 'jamais utilisé' }}
        </v-list-item-subtitle>
        <template #append>
          <v-btn v-if="confirmId !== token.id" variant="text" color="error" @click="confirmId = token.id"
            >Révoquer</v-btn
          >
          <v-btn v-else variant="flat" color="error" @click="revoke(token.id)">Confirmer</v-btn>
        </template>
      </v-list-item>
    </v-list>
    <v-card-text v-else class="text-medium-emphasis">Aucun jeton.</v-card-text>
  </v-card>

  <WebhooksCard v-if="auth.isAdmin" class="mt-8" />
</template>
