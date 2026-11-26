<script setup lang="ts">
import { computed } from 'vue'
import { mdiCloudUploadOutline, mdiDeleteOutline } from '@mdi/js'
import { useOfflineStore } from '@/stores/offline'
import { useReferenceStore } from '@/stores/reference'
import { formatDate, formatSignedAmount } from '@/utils/format'

/** Opérations saisies hors ligne (L4), affichées avec leur état jusqu'à leur envoi. */
const offline = useOfflineStore()
const reference = useReferenceStore()

const items = computed(() =>
  offline.userItems.map((item) => ({
    ...item,
    amount: item.request.lines.reduce((sum, l) => sum + l.amount, 0),
    title: item.request.payeeName || item.request.label || 'Sans tiers',
    account: reference.accountById.get(item.request.accountId)?.name ?? 'Compte inconnu',
  })),
)
</script>

<template>
  <v-card v-if="items.length > 0" class="mb-4" variant="outlined" data-testid="offline-queue">
    <v-card-title class="d-flex align-center ga-2 text-subtitle-1">
      <v-icon :icon="mdiCloudUploadOutline" />
      Saisies hors ligne
      <v-spacer />
      <v-btn
        v-if="offline.online && offline.pendingCount > 0"
        size="small"
        variant="tonal"
        :loading="offline.syncing"
        @click="offline.flush()"
        >Envoyer</v-btn
      >
    </v-card-title>
    <v-list density="compact">
      <v-list-item v-for="item in items" :key="item.id" :title="item.title">
        <template #subtitle>
          {{ formatDate(item.request.date) }} · {{ item.account }}
          <div v-if="item.status === 'rejected'" class="text-error text-wrap">{{ item.error }}</div>
        </template>
        <template #append>
          <div class="d-flex align-center ga-2">
            <span class="text-body-2 font-weight-medium">{{ formatSignedAmount(item.amount) }}</span>
            <v-chip
              size="small"
              :color="item.status === 'rejected' ? 'error' : 'warning'"
              variant="tonal"
              data-testid="queue-status"
              >{{ item.status === 'rejected' ? 'refusée' : 'en attente' }}</v-chip
            >
            <v-btn
              :icon="mdiDeleteOutline"
              size="small"
              variant="text"
              :aria-label="`Supprimer la saisie ${item.title}`"
              @click="offline.remove(item.id)"
            />
          </div>
        </template>
      </v-list-item>
    </v-list>
  </v-card>
</template>
