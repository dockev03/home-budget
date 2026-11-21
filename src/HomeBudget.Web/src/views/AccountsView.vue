<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { mdiPencil, mdiPlus } from '@mdi/js'
import { ApiError } from '@/api/errors'
import AccountDialog from '@/components/AccountDialog.vue'
import { useAuthStore } from '@/stores/auth'
import { useReferenceStore, type Account } from '@/stores/reference'
import { formatAmount } from '@/utils/format'
import { iconPath } from '@/utils/icons'
import { accountTypes } from '@/utils/labels'

const reference = useReferenceStore()
const auth = useAuthStore()
const showArchived = ref(false)
const dialog = ref(false)
const editing = ref<Account | null>(null)
const error = ref<string | null>(null)

const groups = computed(() =>
  accountTypes
    .map((type) => ({
      ...type,
      accounts: reference.accounts.filter(
        (a) => a.type === type.value && (showArchived.value || !a.isArchived),
      ),
    }))
    .filter((g) => g.accounts.length > 0),
)

function edit(account: Account | null) {
  editing.value = account
  dialog.value = true
}

async function reload() {
  try {
    await reference.loadAccounts()
  } catch (e) {
    error.value = e instanceof ApiError ? e.message : 'Chargement impossible.'
  }
}

onMounted(() => reference.load(true).catch(() => (error.value = 'Chargement impossible.')))
</script>

<template>
  <div class="d-flex align-center mb-4">
    <h1 class="text-h5 font-weight-bold">Comptes</h1>
    <v-spacer />
    <v-btn v-if="auth.canWrite" color="primary" :prepend-icon="mdiPlus" @click="edit(null)"
      >Nouveau compte</v-btn
    >
  </div>

  <v-alert v-if="error" type="error" variant="tonal" class="mb-4" :text="error" />

  <v-card class="mb-4" color="primary" variant="tonal">
    <v-card-text class="d-flex align-center">
      <span class="text-subtitle-1">Patrimoine net</span>
      <v-spacer />
      <span class="text-h6 font-weight-bold" data-testid="net-worth">{{
        formatAmount(reference.netWorth)
      }}</span>
    </v-card-text>
  </v-card>

  <v-alert
    v-if="reference.accounts.length === 0"
    type="info"
    variant="tonal"
    text="Créez votre premier compte : son solde initial à une date sert de point de départ."
  />

  <v-card v-for="group in groups" :key="group.value" class="mb-4">
    <v-card-subtitle class="pt-3">{{ group.title }}</v-card-subtitle>
    <v-list lines="one">
      <v-list-item
        v-for="account in group.accounts"
        :key="account.id"
        :to="{ name: 'transactions', query: { accountId: account.id } }"
        :class="{ 'text-disabled': account.isArchived }"
      >
        <template #prepend>
          <v-avatar :color="account.color ?? 'primary'" variant="tonal">
            <v-icon :icon="iconPath(account.icon ?? group.icon)" />
          </v-avatar>
        </template>
        <v-list-item-title>
          {{ account.name }}
          <v-chip v-if="account.isArchived" size="x-small" class="ml-2">Archivé</v-chip>
        </v-list-item-title>
        <v-list-item-subtitle v-if="account.futureBalance !== account.balance">
          Avec les opérations à venir : {{ formatAmount(account.futureBalance) }}
        </v-list-item-subtitle>
        <template #append>
          <span class="font-weight-medium mr-2" :class="account.balance < 0 ? 'text-error' : ''">
            {{ formatAmount(account.balance) }}
          </span>
          <v-btn
            v-if="auth.canWrite"
            :icon="mdiPencil"
            variant="text"
            size="small"
            :aria-label="`Modifier ${account.name}`"
            @click.prevent.stop="edit(account)"
          />
        </template>
      </v-list-item>
    </v-list>
  </v-card>

  <v-switch v-model="showArchived" label="Afficher les comptes archivés" color="primary" hide-details />

  <AccountDialog v-model="dialog" :account="editing" @saved="reload" />
</template>
