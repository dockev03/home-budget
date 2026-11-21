<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useDisplay } from 'vuetify'
import {
  mdiApi,
  mdiHomeOutline,
  mdiLogout,
  mdiShieldLockOutline,
  mdiThemeLightDark,
  mdiWeatherNight,
  mdiWhiteBalanceSunny,
} from '@mdi/js'
import { useAuthStore } from '@/stores/auth'
import { useThemePreference } from '@/composables/useThemePreference'

const auth = useAuthStore()
const router = useRouter()
const { mdAndUp } = useDisplay()
const { preference } = useThemePreference()
const loggingOut = ref(false)

const navigation = [
  { title: 'Accueil', icon: mdiHomeOutline, to: { name: 'home' } },
  { title: 'Sécurité', icon: mdiShieldLockOutline, to: { name: 'security' } },
]

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
        <v-list-item :title="auth.user?.userName" :subtitle="auth.isAdmin ? 'Administrateur' : 'Membre'" />
        <v-divider />
        <v-list-item
          v-if="auth.isAdmin"
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
        exact
      />
    </v-list>
  </v-navigation-drawer>

  <v-main>
    <v-container class="py-6" style="max-width: 1100px">
      <slot />
    </v-container>
  </v-main>

  <v-bottom-navigation v-if="!mdAndUp" grow>
    <v-btn v-for="item in navigation" :key="item.title" :to="item.to" exact>
      <v-icon :icon="item.icon" />
      <span>{{ item.title }}</span>
    </v-btn>
  </v-bottom-navigation>
</template>
