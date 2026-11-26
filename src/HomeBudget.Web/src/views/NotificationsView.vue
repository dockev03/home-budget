<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiBellOutline, mdiCellphone, mdiDeleteOutline, mdiEmailOutline } from '@mdi/js'
import { api, type Schemas } from '@/api/client'
import { ApiError, unwrap } from '@/api/errors'
import { base64UrlToBytes, deviceLabel, pushSupport, type PushSupport } from '@/utils/push'
import { formatDateTime } from '@/utils/users'

type Settings = Schemas['NotificationSettingsDto']
type Frequency = Schemas['PreferencesDto']['emailSummary']

const settings = ref<Settings | null>(null)
const preferences = ref<Schemas['PreferencesDto'] | null>(null)
const email = ref('')
const error = ref<string | null>(null)
const message = ref<string | null>(null)
const busy = ref(false)
const valid = ref(false)
/** Abonnement push de ce navigateur, s'il existe. */
const currentEndpoint = ref<string | null>(null)

const support = computed<PushSupport>(() =>
  pushSupport({
    userAgent: navigator.userAgent,
    hasServiceWorker: 'serviceWorker' in navigator,
    hasPushManager: 'PushManager' in window,
    standalone: window.matchMedia?.('(display-mode: standalone)').matches ?? false,
    permission: 'Notification' in window ? Notification.permission : 'unavailable',
  }),
)

const thisDeviceSubscribed = computed(
  () =>
    !!currentEndpoint.value && !!settings.value?.devices.some((d) => d.endpoint === currentEndpoint.value),
)

const alertTypes = [
  {
    key: 'budgetAlerts',
    title: 'Budget',
    subtitle: 'Catégorie à 80 % puis à 100 % de son budget (seuils du foyer).',
  },
  {
    key: 'forecastAlerts',
    title: 'Solde prévu',
    subtitle: 'Passage prévu sous le seuil des comptes de la prévision.',
  },
  {
    key: 'dueTomorrowAlerts',
    title: 'Échéances de demain',
    subtitle: 'Prélèvements et revenus récurrents du lendemain.',
  },
  { key: 'pendingAlerts', title: 'À valider', subtitle: 'Échéances qui attendent votre confirmation.' },
] as const

const frequencies: { value: Frequency; title: string }[] = [
  { value: 'None', title: 'Jamais' },
  { value: 'Weekly', title: 'Chaque semaine (le lundi)' },
  { value: 'Monthly', title: 'Chaque mois (le 1er)' },
]

const emailRules = [
  (v: string) =>
    !!v.trim() || preferences.value?.emailSummary === 'None' || 'Adresse requise pour le résumé.',
  (v: string) => !v.trim() || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim()) || 'Adresse e-mail invalide.',
]

async function guarded(action: () => Promise<void>) {
  busy.value = true
  error.value = null
  message.value = null
  try {
    await action()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : e instanceof Error ? e.message : 'Opération impossible.'
  } finally {
    busy.value = false
  }
}

function apply(result: Settings) {
  settings.value = result
  preferences.value = { ...result.preferences }
  email.value = result.email ?? ''
}

async function registration(): Promise<ServiceWorkerRegistration | null> {
  if (!('serviceWorker' in navigator)) return null
  return (await navigator.serviceWorker.getRegistration()) ?? null
}

async function load() {
  await guarded(async () => {
    apply(await unwrap(api.GET('/api/notifications')))
    const subscription = await (await registration())?.pushManager.getSubscription()
    currentEndpoint.value = subscription?.endpoint ?? null
  })
}

const savePreferences = () =>
  guarded(async () => {
    if (!valid.value || !preferences.value) return
    apply(
      await unwrap(
        api.PUT('/api/notifications/preferences', {
          body: { ...preferences.value, email: email.value.trim() || null },
        }),
      ),
    )
    message.value = 'Préférences enregistrées.'
  })

const subscribe = () =>
  guarded(async () => {
    const key = settings.value?.vapidPublicKey
    const sw = await registration()
    if (!key || !sw) throw new Error('Service worker indisponible : rechargez la page puis réessayez.')
    if ((await Notification.requestPermission()) !== 'granted') {
      throw new Error('Notifications refusées : autorisez-les dans les réglages du navigateur.')
    }
    // Un abonnement fait avec une autre clé (clés VAPID changées) est remplacé.
    const existing = await sw.pushManager.getSubscription()
    if (existing) await existing.unsubscribe()
    const subscription = await sw.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: base64UrlToBytes(key),
    })
    const json = subscription.toJSON()
    await unwrap(
      api.POST('/api/notifications/subscriptions', {
        body: {
          endpoint: subscription.endpoint,
          keys: { p256dh: json.keys?.p256dh ?? '', auth: json.keys?.auth ?? '' },
          device: deviceLabel(navigator.userAgent),
        },
      }),
    )
    currentEndpoint.value = subscription.endpoint
    apply(await unwrap(api.GET('/api/notifications')))
    message.value = 'Cet appareil recevra les notifications.'
  })

const unsubscribe = () =>
  guarded(async () => {
    const subscription = await (await registration())?.pushManager.getSubscription()
    if (subscription) {
      await unwrap(
        api.POST('/api/notifications/subscriptions/unsubscribe', {
          body: { endpoint: subscription.endpoint },
        }),
      )
      await subscription.unsubscribe()
    }
    currentEndpoint.value = null
    apply(await unwrap(api.GET('/api/notifications')))
  })

const removeDevice = (id: string) =>
  guarded(async () => {
    await unwrap(api.DELETE('/api/notifications/subscriptions/{id}', { params: { path: { id } } }))
    apply(await unwrap(api.GET('/api/notifications')))
  })

const testPush = () =>
  guarded(async () => {
    const result = await unwrap(api.POST('/api/notifications/test'))
    message.value =
      result.reached === 0
        ? 'Aucun appareil n’a reçu la notification.'
        : `Notification envoyée à ${result.reached} appareil${result.reached > 1 ? 's' : ''}.`
  })

const testEmail = () =>
  guarded(async () => {
    await unwrap(api.POST('/api/notifications/test-email'))
    message.value = `E-mail d’essai envoyé à ${settings.value?.email}.`
  })

onMounted(load)
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Notifications</h1>

  <v-alert
    v-if="error"
    type="error"
    variant="tonal"
    class="mb-4"
    :text="error"
    closable
    @click:close="error = null"
  />
  <v-alert
    v-if="message"
    type="success"
    variant="tonal"
    class="mb-4"
    :text="message"
    closable
    data-testid="notification-message"
    @click:close="message = null"
  />

  <template v-if="settings && preferences">
    <v-card class="mb-4" title="Notifications push" :prepend-icon="mdiBellOutline">
      <v-card-text>
        <v-alert
          v-if="!settings.pushAvailable"
          type="info"
          variant="tonal"
          text="Le push n’est pas configuré sur le serveur (clés VAPID absentes)."
        />
        <template v-else>
          <v-alert
            v-if="support === 'ios-not-installed'"
            type="info"
            variant="tonal"
            class="mb-4"
            text="Sur iPhone, ajoutez d’abord l’appli à l’écran d’accueil (Partager › Sur l’écran d’accueil), puis ouvrez-la depuis son icône."
          />
          <v-alert
            v-else-if="support === 'unsupported'"
            type="info"
            variant="tonal"
            class="mb-4"
            text="Ce navigateur ne gère pas les notifications push."
          />
          <v-alert
            v-else-if="support === 'denied'"
            type="warning"
            variant="tonal"
            class="mb-4"
            text="Les notifications sont bloquées pour ce site : autorisez-les dans les réglages du navigateur."
          />

          <div class="d-flex flex-wrap ga-2 mb-4">
            <v-btn
              v-if="!thisDeviceSubscribed"
              color="primary"
              :disabled="support !== 'supported'"
              :loading="busy"
              @click="subscribe"
            >
              Activer sur cet appareil
            </v-btn>
            <v-btn v-else variant="tonal" :loading="busy" @click="unsubscribe"
              >Désactiver sur cet appareil</v-btn
            >
            <v-btn variant="text" :disabled="settings.devices.length === 0" :loading="busy" @click="testPush">
              Envoyer un essai
            </v-btn>
          </div>

          <v-list
            v-if="settings.devices.length > 0"
            density="compact"
            class="mb-2"
            data-testid="push-devices"
          >
            <v-list-subheader>Appareils abonnés</v-list-subheader>
            <v-list-item
              v-for="device in settings.devices"
              :key="device.id"
              :prepend-icon="mdiCellphone"
              :title="
                (device.device ?? 'Appareil') + (device.endpoint === currentEndpoint ? ' (cet appareil)' : '')
              "
              :subtitle="
                device.lastSuccessAt
                  ? `Dernier envoi réussi : ${formatDateTime(device.lastSuccessAt)}`
                  : `Ajouté le ${formatDateTime(device.createdAt)}`
              "
            >
              <template #append>
                <v-btn
                  :icon="mdiDeleteOutline"
                  variant="text"
                  size="small"
                  :aria-label="`Retirer ${device.device ?? 'l’appareil'}`"
                  @click="removeDevice(device.id)"
                />
              </template>
            </v-list-item>
          </v-list>
        </template>
      </v-card-text>
    </v-card>

    <v-form v-model="valid" @submit.prevent="savePreferences">
      <v-card class="mb-4" title="Alertes reçues">
        <v-card-text>
          <v-switch
            v-for="type in alertTypes"
            :key="type.key"
            v-model="preferences[type.key]"
            :label="type.title"
            :messages="type.subtitle"
            color="primary"
            class="mb-2"
            inset
          />
          <p class="text-body-2 text-medium-emphasis mt-2">
            Les alertes sont vérifiées toutes les heures, de 8 h à 22 h, et ne sont envoyées qu’une fois.
          </p>
        </v-card-text>
      </v-card>

      <v-card class="mb-4" title="Résumé par e-mail" :prepend-icon="mdiEmailOutline">
        <v-card-text>
          <v-alert
            v-if="!settings.emailAvailable"
            type="info"
            variant="tonal"
            class="mb-4"
            text="L’envoi d’e-mails n’est pas configuré sur le serveur."
          />
          <v-select
            v-model="preferences.emailSummary"
            :items="frequencies"
            label="Fréquence"
            :disabled="!settings.emailAvailable"
            data-testid="summary-frequency"
          />
          <v-text-field
            v-model="email"
            label="Adresse de réception"
            type="email"
            autocomplete="email"
            :rules="emailRules"
            data-testid="summary-email"
          />
          <v-btn
            variant="text"
            :disabled="!settings.emailAvailable || !settings.email"
            :loading="busy"
            @click="testEmail"
          >
            Envoyer un e-mail d’essai
          </v-btn>
        </v-card-text>
      </v-card>

      <v-btn type="submit" color="primary" :loading="busy" :disabled="!valid">Enregistrer</v-btn>
    </v-form>
  </template>
</template>
