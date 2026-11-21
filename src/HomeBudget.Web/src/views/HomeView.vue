<script setup lang="ts">
import { mdiShieldAlertOutline } from '@mdi/js'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
</script>

<template>
  <h1 class="text-h5 font-weight-bold mb-4">Bonjour {{ auth.user?.userName }}</h1>

  <v-alert
    v-if="auth.user && !auth.user.twoFactorEnabled"
    type="warning"
    variant="tonal"
    :icon="mdiShieldAlertOutline"
    class="mb-4"
    title="Double authentification désactivée"
  >
    Protégez l'accès à vos comptes en activant la double authentification.
    <template #append>
      <v-btn :to="{ name: 'security' }" variant="flat" color="warning">Activer</v-btn>
    </template>
  </v-alert>

  <v-card>
    <v-card-title>Tableau de bord</v-card-title>
    <v-card-text class="text-medium-emphasis">
      Le socle de l'application est en place. Les comptes, les opérations, le budget et la prévision arrivent
      à l'étape suivante.
    </v-card-text>
  </v-card>
</template>
