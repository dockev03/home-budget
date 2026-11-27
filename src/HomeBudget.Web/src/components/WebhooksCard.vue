<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiContentCopy, mdiPlus } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ifMatch } from '@/api/concurrency'
import { ApiError, unwrap } from '@/api/errors'
import { deliveryStatus, webhookEventLabel, webhookUrlError, type DeliveryStatus } from '@/utils/integrations'
import { required } from '@/utils/rules'
import { formatDateTime } from '@/utils/users'

type Webhook = Schemas['WebhookDto']
type Delivery = Schemas['WebhookDeliveryDto']

/** Webhooks (I5), réservés aux administrateurs : messages signés envoyés à chaque événement choisi. */
const webhooks = ref<Webhook[]>([])
const events = ref<string[]>([])
const error = ref<string | null>(null)
const dialog = ref(false)
const editing = ref<Webhook | null>(null)
const form = ref({ name: '', url: '', events: [] as string[], isActive: true })
const valid = ref(false)
const saving = ref(false)
const formError = ref<string | null>(null)
/** Secret tout juste créé ou régénéré : montré une seule fois. */
const secret = ref<{ name: string; value: string } | null>(null)
const deliveries = ref<Record<string, Delivery[] | undefined>>({})
const testing = ref<string | null>(null)
const confirmDelete = ref<string | null>(null)
const eventOptions = computed(() => events.value.map((value) => ({ value, title: webhookEventLabel(value) })))

async function load() {
  try {
    ;[webhooks.value, events.value] = await Promise.all([
      unwrap(api.GET('/api/webhooks')),
      unwrap(api.GET('/api/webhooks/events')),
    ])
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

function openDialog(webhook: Webhook | null) {
  editing.value = webhook
  form.value = webhook
    ? { name: webhook.name, url: webhook.url, events: [...webhook.events], isActive: webhook.isActive }
    : { name: '', url: '', events: [...events.value], isActive: true }
  formError.value = null
  dialog.value = true
}

async function save() {
  saving.value = true
  formError.value = null
  try {
    if (editing.value) {
      const updated = await unwrap(
        api.PUT('/api/webhooks/{id}', {
          params: { path: { id: editing.value.id } },
          body: form.value,
          headers: ifMatch(editing.value.version),
        }),
      )
      webhooks.value = webhooks.value.map((w) => (w.id === updated.id ? updated : w))
    } else {
      const created = await unwrap(api.POST('/api/webhooks', { body: form.value }))
      webhooks.value.push(created.item)
      secret.value = { name: created.item.name, value: created.secret }
    }
    dialog.value = false
  } catch (e) {
    formError.value = e instanceof ApiError ? e.message : 'Enregistrement impossible.'
  } finally {
    saving.value = false
  }
}

async function remove(webhook: Webhook) {
  error.value = null
  try {
    await unwrap(
      api.DELETE('/api/webhooks/{id}', {
        params: { path: { id: webhook.id } },
        headers: ifMatch(webhook.version),
      }),
    )
    webhooks.value = webhooks.value.filter((w) => w.id !== webhook.id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Suppression impossible.'
  } finally {
    confirmDelete.value = null
  }
}

async function regenerate(webhook: Webhook) {
  error.value = null
  try {
    const result = await unwrap(
      api.POST('/api/webhooks/{id}/secret', { params: { path: { id: webhook.id } } }),
    )
    secret.value = { name: webhook.name, value: result.secret }
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Régénération impossible.'
  }
}

async function test(webhook: Webhook) {
  testing.value = webhook.id
  error.value = null
  try {
    await unwrap(api.POST('/api/webhooks/{id}/test', { params: { path: { id: webhook.id } } }))
    await loadDeliveries(webhook.id)
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Test impossible.'
  } finally {
    testing.value = null
  }
}

async function loadDeliveries(id: string) {
  deliveries.value[id] = await unwrap(api.GET('/api/webhooks/{id}/deliveries', { params: { path: { id } } }))
}

function toggleDeliveries(id: string) {
  if (deliveries.value[id]) deliveries.value[id] = undefined
  else void loadDeliveries(id).catch(() => (error.value = 'Chargement de l’historique impossible.'))
}

async function copySecret() {
  if (secret.value) await navigator.clipboard.writeText(secret.value.value)
}

function status(delivery: Delivery) {
  return deliveryStatus[delivery.status as DeliveryStatus]
}

onMounted(load)
</script>

<template>
  <v-card title="Webhooks" data-testid="webhooks">
    <template #append>
      <v-btn :prepend-icon="mdiPlus" color="primary" variant="tonal" @click="openDialog(null)">Ajouter</v-btn>
    </template>
    <v-card-text>
      <p class="mb-2 text-medium-emphasis">
        À chaque événement choisi, un message JSON est envoyé en POST à l'adresse, signé par
        <code>X-HomeBudget-Signature</code> (HMAC-SHA256 du secret sur « horodatage.corps »). Il ne contient
        que les comptes que vous voyez. Après un échec, l'envoi est retenté (1 min, 5 min, 30 min, 2 h, 12 h)
        ; au bout de 5 envois abandonnés à la suite, le webhook est désactivé.
      </p>
      <v-alert v-if="error" type="error" variant="tonal" class="mb-2" :text="error" />
      <v-alert
        v-if="secret"
        type="success"
        variant="tonal"
        class="mb-2"
        closable
        @click:close="secret = null"
      >
        <p class="mb-2">
          Secret de « {{ secret.name }} » : copiez-le maintenant, <strong>il ne sera plus affiché</strong>.
        </p>
        <div class="d-flex align-center flex-wrap ga-2">
          <code class="text-body-2" data-testid="webhook-secret" style="word-break: break-all">{{
            secret.value
          }}</code>
          <v-btn :prepend-icon="mdiContentCopy" size="small" variant="tonal" @click="copySecret"
            >Copier</v-btn
          >
        </div>
      </v-alert>
    </v-card-text>
    <v-list v-if="webhooks.length > 0" lines="two">
      <template v-for="webhook in webhooks" :key="webhook.id">
        <v-list-item :title="webhook.name">
          <v-list-item-subtitle>
            <span style="word-break: break-all">{{ webhook.url }}</span> ·
            {{ webhook.events.map(webhookEventLabel).join(', ') }}
          </v-list-item-subtitle>
          <template #prepend>
            <v-chip
              :color="webhook.isActive ? 'success' : 'error'"
              size="small"
              variant="tonal"
              class="mr-3"
              >{{ webhook.isActive ? 'Actif' : 'Inactif' }}</v-chip
            >
          </template>
          <div class="d-flex flex-wrap ga-1 mt-1">
            <v-btn size="small" variant="text" :loading="testing === webhook.id" @click="test(webhook)"
              >Tester</v-btn
            >
            <v-btn size="small" variant="text" @click="toggleDeliveries(webhook.id)">{{
              deliveries[webhook.id] ? 'Masquer les envois' : 'Envois'
            }}</v-btn>
            <v-btn size="small" variant="text" @click="openDialog(webhook)">Modifier</v-btn>
            <v-btn size="small" variant="text" @click="regenerate(webhook)">Nouveau secret</v-btn>
            <v-btn
              v-if="confirmDelete !== webhook.id"
              size="small"
              variant="text"
              color="error"
              @click="confirmDelete = webhook.id"
              >Supprimer</v-btn
            >
            <v-btn v-else size="small" variant="flat" color="error" @click="remove(webhook)">Confirmer</v-btn>
          </div>
        </v-list-item>
        <v-table v-if="deliveries[webhook.id]" density="compact" class="mx-4 mb-2">
          <tbody>
            <tr v-if="deliveries[webhook.id]!.length === 0">
              <td class="text-medium-emphasis">Aucun envoi.</td>
            </tr>
            <tr v-for="delivery in deliveries[webhook.id]" :key="delivery.id">
              <td class="text-no-wrap">{{ formatDateTime(delivery.createdAt) }}</td>
              <td>{{ webhookEventLabel(delivery.event) }}</td>
              <td>
                <v-chip :color="status(delivery).color" size="x-small" variant="tonal">{{
                  status(delivery).label
                }}</v-chip>
              </td>
              <td class="text-caption">
                {{ delivery.statusCode ?? '' }} {{ delivery.error ?? '' }}
                <span v-if="delivery.nextAttemptAt"
                  >· nouvel essai {{ formatDateTime(delivery.nextAttemptAt) }}</span
                >
              </td>
            </tr>
          </tbody>
        </v-table>
      </template>
    </v-list>
    <v-card-text v-else class="text-medium-emphasis">Aucun webhook.</v-card-text>
  </v-card>

  <v-dialog v-model="dialog" max-width="560">
    <v-card :title="editing ? 'Modifier le webhook' : 'Nouveau webhook'">
      <v-form v-model="valid" @submit.prevent="save">
        <v-card-text>
          <v-alert
            v-if="formError"
            type="error"
            variant="tonal"
            density="compact"
            class="mb-4"
            :text="formError"
          />
          <v-text-field v-model="form.name" label="Nom" :rules="[required]" maxlength="60" />
          <v-text-field
            v-model="form.url"
            label="Adresse"
            placeholder="https://n8n.maison.lan/webhook/budget"
            :rules="[required, webhookUrlError]"
            hint="Réseau local autorisé ; boucle locale et adresses de métadonnées refusées."
            persistent-hint
          />
          <p class="text-subtitle-2 mt-4">Événements</p>
          <v-checkbox
            v-for="option in eventOptions"
            :key="option.value"
            v-model="form.events"
            :value="option.value"
            :label="option.title"
            density="compact"
            hide-details
          />
          <v-switch
            v-if="editing"
            v-model="form.isActive"
            label="Actif"
            color="primary"
            class="mt-2"
            hide-details
          />
        </v-card-text>
        <v-card-actions>
          <v-spacer />
          <v-btn @click="dialog = false">Annuler</v-btn>
          <v-btn
            type="submit"
            color="primary"
            variant="flat"
            :loading="saving"
            :disabled="!valid || form.events.length === 0"
            >Enregistrer</v-btn
          >
        </v-card-actions>
      </v-form>
    </v-card>
  </v-dialog>
</template>
