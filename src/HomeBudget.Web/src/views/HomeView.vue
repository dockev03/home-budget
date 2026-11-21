<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { mdiShieldAlertOutline } from '@mdi/js'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore } from '@/stores/reference'
import { formatAmount } from '@/utils/format'
import { iconPath } from '@/utils/icons'

const auth = useAuthStore()
const reference = useReferenceStore()
const error = ref<string | null>(null)

onMounted(() => reference.load(true).catch(() => (error.value = 'Chargement impossible.')))
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

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <v-card class="mb-4">
    <v-card-title class="d-flex align-center">
      Comptes
      <v-spacer />
      <span class="text-body-1"
        >Patrimoine net : <strong>{{ formatAmount(reference.netWorth) }}</strong></span
      >
    </v-card-title>
    <v-list v-if="reference.activeAccounts.length">
      <v-list-item
        v-for="account in reference.activeAccounts"
        :key="account.id"
        :title="account.name"
        :to="{ name: 'transactions', query: { accountId: account.id } }"
      >
        <template #prepend>
          <v-avatar :color="account.color ?? 'primary'" variant="tonal" size="36">
            <v-icon :icon="iconPath(account.icon)" size="20" />
          </v-avatar>
        </template>
        <template #append>
          <span class="font-weight-medium" :class="account.balance < 0 ? 'text-error' : ''">
            {{ formatAmount(account.balance) }}
          </span>
        </template>
      </v-list-item>
    </v-list>
    <v-card-text v-else class="text-medium-emphasis">
      Aucun compte pour l'instant.
      <v-btn :to="{ name: 'accounts' }" variant="text" color="primary">Créer un compte</v-btn>
    </v-card-text>
  </v-card>

  <v-card>
    <v-card-title>Tableau de bord</v-card-title>
    <v-card-text class="text-medium-emphasis">
      Le budget, la prévision de fin de mois et les graphiques arriveront dans les prochaines étapes.
    </v-card-text>
  </v-card>
</template>
