<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import {
  mdiChartBar,
  mdiAccountGroupOutline,
  mdiApi,
  mdiBellOutline,
  mdiCloudOffOutline,
  mdiBankOutline,
  mdiCalendarCheckOutline,
  mdiCalendarMonthOutline,
  mdiChartSankey,
  mdiCallSplit,
  mdiCalendarSync,
  mdiCogOutline,
  mdiChartDonut,
  mdiClipboardTextClockOutline,
  mdiFileUploadOutline,
  mdiFormatListBulleted,
  mdiHandCoinOutline,
  mdiHomeOutline,
  mdiPlus,
  mdiRepeat,
  mdiScaleBalance,
  mdiShapeOutline,
  mdiLogout,
  mdiPiggyBankOutline,
  mdiKeyChainVariant,
  mdiShieldLockOutline,
  mdiTimelineQuestionOutline,
  mdiThemeLightDark,
  mdiWeatherNight,
  mdiWhiteBalanceSunny,
} from '@mdi/js'
import TransactionDialog from '@/components/TransactionDialog.vue'
import { useAuthStore } from '@/stores/auth'
import { useOfflineStore } from '@/stores/offline'
import { usePendingStore } from '@/stores/pending'
import { useReferenceStore } from '@/stores/reference'
import { useTransactionEditor } from '@/stores/transactionEditor'
import { useThemePreference } from '@/composables/useThemePreference'
import { roleLabel } from '@/utils/users'
import { api } from '@/api/client'
import { clearApiCache, warmUpCache } from '@/offline/cache'
import { applyUpdate, updateAvailable } from '@/pwa'

const auth = useAuthStore()
const reference = useReferenceStore()
const pending = usePendingStore()
const editor = useTransactionEditor()
const offline = useOfflineStore()
const router = useRouter()
const { mdAndUp } = useDisplay()
const { preference } = useThemePreference()
const loggingOut = ref(false)

// Badge « À valider » : rafraîchi aussi par l'accueil et par l'écran des échéances.
onMounted(() => pending.load().catch(() => undefined))

// Hors ligne (L4) : rejeu de la file d'attente, et données des dernières périodes gardées pour la consultation.
onMounted(async () => {
  offline.start()
  if (!navigator.onLine) return
  const { data } = await api.GET('/api/periods/current').catch(() => ({ data: undefined }))
  if (data) await warmUpCache(data.key)
})

const syncMessage = computed(() => {
  const result = offline.lastResult
  if (!result) return null
  const parts = []
  if (result.created > 0)
    parts.push(
      `${result.created} opération${result.created > 1 ? 's' : ''} saisie${result.created > 1 ? 's' : ''} hors ligne envoyée${result.created > 1 ? 's' : ''}`,
    )
  if (result.rejected > 0)
    parts.push(`${result.rejected} refusée${result.rejected > 1 ? 's' : ''} par le serveur (voir Opérations)`)
  return parts.join(' ; ') + '.'
})

// Menu du bas (mobile) : 5 entrées au plus ; les autres sont dans le menu du compte.
const navigation = computed(() =>
  [
    { title: 'Accueil', icon: mdiHomeOutline, to: { name: 'home' }, exact: true, bottom: true },
    {
      title: 'Opérations',
      icon: mdiFormatListBulleted,
      to: { name: 'transactions' },
      exact: true,
      bottom: true,
    },
    { title: 'Budget', icon: mdiChartDonut, to: { name: 'budget' }, exact: false, bottom: true },
    { title: 'Bilan annuel', icon: mdiChartBar, to: { name: 'annual' }, exact: false, bottom: false },
    { title: 'Rapports', icon: mdiChartSankey, to: { name: 'reports' }, exact: true, bottom: false },
    { title: 'Comptes', icon: mdiBankOutline, to: { name: 'accounts' }, exact: true, bottom: true },
    { title: 'Catégories', icon: mdiShapeOutline, to: { name: 'categories' }, exact: true, bottom: true },
    {
      title: 'Importer',
      icon: mdiFileUploadOutline,
      to: { name: 'import' },
      exact: true,
      bottom: false,
      write: true,
    },
    { title: 'Récurrences', icon: mdiCalendarSync, to: { name: 'recurrences' }, exact: true, bottom: false },
    {
      title: 'Calendrier',
      icon: mdiCalendarMonthOutline,
      to: { name: 'calendar' },
      exact: false,
      bottom: false,
    },
    {
      title: 'À valider',
      icon: mdiCalendarCheckOutline,
      to: { name: 'pending' },
      exact: true,
      bottom: false,
      badge: pending.count,
    },
    {
      title: 'Scénarios',
      icon: mdiTimelineQuestionOutline,
      to: { name: 'scenarios' },
      exact: true,
      bottom: false,
    },
    { title: 'Objectifs', icon: mdiPiggyBankOutline, to: { name: 'savings' }, exact: true, bottom: false },
    { title: 'Abonnements', icon: mdiRepeat, to: { name: 'subscriptions' }, exact: true, bottom: false },
    {
      title: 'Rapprochement',
      icon: mdiScaleBalance,
      to: { name: 'reconciliation' },
      exact: true,
      bottom: false,
    },
    { title: 'Avances', icon: mdiHandCoinOutline, to: { name: 'advances' }, exact: true, bottom: false },
    { title: 'Répartition', icon: mdiCallSplit, to: { name: 'split' }, exact: false, bottom: false },
    {
      title: 'Notifications',
      icon: mdiBellOutline,
      to: { name: 'notifications' },
      exact: true,
      bottom: false,
    },
    { title: 'Sécurité', icon: mdiShieldLockOutline, to: { name: 'security' }, exact: true, bottom: false },
    { title: 'Accès API', icon: mdiKeyChainVariant, to: { name: 'api-access' }, exact: true, bottom: false },
    {
      title: 'Paramètres',
      icon: mdiCogOutline,
      to: { name: 'settings' },
      exact: true,
      bottom: false,
      admin: true,
    },
    {
      title: 'Utilisateurs',
      icon: mdiAccountGroupOutline,
      to: { name: 'users' },
      exact: true,
      bottom: false,
      admin: true,
    },
    {
      title: 'Journal d’audit',
      icon: mdiClipboardTextClockOutline,
      to: { name: 'audit' },
      exact: true,
      bottom: false,
      admin: true,
    },
  ].filter((item) => (!item.admin || auth.isAdmin) && (!item.write || auth.canWrite)),
)
const bottomNavigation = computed(() => navigation.value.filter((item) => item.bottom))
const menuNavigation = computed(() => navigation.value.filter((item) => !item.bottom))

const themeOptions = [
  { value: 'light', title: 'Clair', icon: mdiWhiteBalanceSunny },
  { value: 'dark', title: 'Sombre', icon: mdiWeatherNight },
  { value: 'system', title: 'Automatique', icon: mdiThemeLightDark },
] as const

const userInitial = computed(() => auth.user?.userName.charAt(0).toUpperCase() ?? '?')

async function logout() {
  loggingOut.value = true
  try {
    await auth.logout()
  } finally {
    // Les données du foyer ne restent pas sur l'appareil (la file d'attente, liée à l'utilisateur, est gardée).
    await clearApiCache().catch(() => undefined)
    reference.reset()
    pending.reset()
    loggingOut.value = false
    await router.push({ name: 'login' })
  }
}
</script>

<template>
  <v-app-bar flat border="b" density="comfortable">
    <template #prepend>
      <img src="/favicon.svg" alt="" width="28" height="28" class="ml-2" />
    </template>
    <v-app-bar-title class="font-weight-bold">Home Budget</v-app-bar-title>

    <v-menu>
      <template #activator="{ props }">
        <v-btn v-bind="props" :icon="mdiThemeLightDark" aria-label="Thème" />
      </template>
      <v-list
        density="compact"
        :selected="[preference]"
        @update:selected="(v) => v[0] && (preference = v[0])"
      >
        <v-list-item
          v-for="option in themeOptions"
          :key="option.value"
          :value="option.value"
          :title="option.title"
          :prepend-icon="option.icon"
        />
      </v-list>
    </v-menu>

    <v-menu>
      <template #activator="{ props }">
        <v-btn v-bind="props" icon aria-label="Compte">
          <v-avatar color="primary" size="32">{{ userInitial }}</v-avatar>
        </v-btn>
      </template>
      <v-list density="compact">
        <v-list-item :title="auth.user?.displayName" :subtitle="roleLabel(auth.user?.roles[0])" />
        <v-divider />
        <template v-if="!mdAndUp">
          <v-list-item
            v-for="item in menuNavigation"
            :key="item.title"
            :to="item.to"
            :prepend-icon="item.icon"
            :title="item.title"
          >
            <template v-if="item.badge" #append>
              <v-badge :content="item.badge" color="warning" inline />
            </template>
          </v-list-item>
        </template>
        <v-list-item
          href="/api/docs/"
          target="_blank"
          rel="noopener"
          :prepend-icon="mdiApi"
          title="Documentation de l'API"
        />
        <v-list-item
          :prepend-icon="mdiLogout"
          title="Se déconnecter"
          :disabled="loggingOut"
          @click="logout"
        />
      </v-list>
    </v-menu>
  </v-app-bar>

  <v-navigation-drawer v-if="mdAndUp" permanent>
    <v-list nav>
      <v-list-item
        v-for="item in navigation"
        :key="item.title"
        :to="item.to"
        :prepend-icon="item.icon"
        :title="item.title"
        :exact="item.exact"
      >
        <template v-if="item.badge" #append>
          <v-badge :content="item.badge" color="warning" inline />
        </template>
      </v-list-item>
    </v-list>
  </v-navigation-drawer>

  <v-main>
    <v-system-bar v-if="!offline.online" color="warning" height="32" data-testid="offline-banner">
      <v-icon :icon="mdiCloudOffOutline" class="mr-2" />
      <span class="text-truncate">
        Hors ligne : consultation seule ; les opérations saisies sont mises en attente
        <template v-if="offline.pendingCount > 0">({{ offline.pendingCount }})</template>.
      </span>
    </v-system-bar>
    <v-container class="py-6" style="max-width: 1100px">
      <slot />
    </v-container>
  </v-main>

  <!-- Saisie rapide (L3) : accessible depuis tous les écrans. -->
  <v-btn
    v-if="auth.canWrite"
    :icon="mdiPlus"
    color="primary"
    size="large"
    elevation="6"
    class="hb-fab"
    :class="{ 'hb-fab--above-nav': !mdAndUp }"
    aria-label="Nouvelle opération"
    @click="editor.open('create', null, ($route.query.accountId as string) ?? null)"
  />
  <TransactionDialog />

  <v-snackbar :model-value="updateAvailable" timeout="-1" location="top">
    Une nouvelle version de l'appli est disponible.
    <template #actions>
      <v-btn color="primary" variant="text" @click="applyUpdate">Mettre à jour</v-btn>
    </template>
  </v-snackbar>
  <v-snackbar
    :model-value="!!syncMessage"
    :color="offline.lastResult?.rejected ? 'warning' : undefined"
    data-testid="sync-message"
    @update:model-value="(v) => !v && (offline.lastResult = null)"
  >
    {{ syncMessage }}
  </v-snackbar>

  <v-bottom-navigation v-if="!mdAndUp" grow>
    <v-btn v-for="item in bottomNavigation" :key="item.title" :to="item.to" :exact="item.exact">
      <v-icon :icon="item.icon" />
      <span>{{ item.title }}</span>
    </v-btn>
  </v-bottom-navigation>
</template>

<style scoped>
.hb-fab {
  position: fixed;
  right: 24px;
  bottom: 24px;
  z-index: 1005;
}

/* Sur mobile, au-dessus du menu du bas. */
.hb-fab--above-nav {
  right: 16px;
  bottom: calc(72px + env(safe-area-inset-bottom));
}
</style>
