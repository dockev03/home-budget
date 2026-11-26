import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api } from '@/api/client'
import { indexedDbStore } from '@/offline/db'
import { flushQueue, type QueuedTransaction, type TransactionRequest } from '@/offline/queue'
import { uuidv7 } from '@/offline/uuid'
import { useAuthStore } from './auth'
import { useTransactionEditor } from './transactionEditor'

/**
 * Hors ligne (L4) : état du réseau, file des opérations saisies sans réseau et rejeu au retour de la connexion
 * (ou à l'ouverture de l'appli : la Background Sync API n'existe pas sur iOS).
 */
export const useOfflineStore = defineStore('offline', () => {
  const online = ref(typeof navigator === 'undefined' ? true : navigator.onLine)
  const items = ref<QueuedTransaction[]>([])
  const syncing = ref(false)
  const lastResult = ref<{ created: number; rejected: number } | null>(null)
  let started = false

  const auth = useAuthStore()
  const userItems = computed(() => items.value.filter((i) => i.userId === auth.user?.id))
  const pendingCount = computed(() => userItems.value.filter((i) => i.status === 'pending').length)
  const rejected = computed(() => userItems.value.filter((i) => i.status === 'rejected'))

  async function refresh() {
    try {
      items.value = (await indexedDbStore.all()).sort((a, b) => b.id.localeCompare(a.id))
    } catch {
      items.value = [] // IndexedDB indisponible (navigation privée de certains navigateurs)
    }
  }

  /** Met une saisie en file ; elle s'affiche aussitôt avec le badge « en attente ». */
  async function enqueue(request: TransactionRequest): Promise<QueuedTransaction> {
    const userId = auth.user?.id
    if (!userId) throw new Error('Session requise.')
    const item: QueuedTransaction = {
      id: uuidv7(),
      userId,
      createdAt: new Date().toISOString(),
      request,
      status: 'pending',
    }
    await indexedDbStore.put(item)
    await refresh()
    return item
  }

  async function remove(id: string) {
    await indexedDbStore.remove(id)
    await refresh()
  }

  async function flush() {
    const userId = auth.user?.id
    if (!userId || syncing.value || !online.value) return
    syncing.value = true
    try {
      const result = await flushQueue(
        indexedDbStore,
        async (batch) => {
          const { data, response } = await api.POST('/api/sync', {
            body: { items: batch.map((i) => ({ id: i.id, transaction: i.request })) },
          })
          return response.ok && data
            ? { ok: true, results: data.items }
            : { ok: false, status: response.status }
        },
        userId,
      )
      if (result.created > 0 || result.rejected > 0) {
        lastResult.value = { created: result.created, rejected: result.rejected }
        useTransactionEditor().revision++ // les écrans se rechargent
      }
    } finally {
      syncing.value = false
      await refresh()
    }
  }

  /** Écoute le réseau et rejoue la file au retour de la connexion et à chaque retour sur l'appli. */
  function start() {
    if (started || typeof window === 'undefined') return
    started = true
    window.addEventListener('online', () => {
      online.value = true
      void flush()
    })
    window.addEventListener('offline', () => (online.value = false))
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'visible') void flush()
    })
    void refresh().then(flush)
  }

  return {
    online,
    items,
    userItems,
    pendingCount,
    rejected,
    syncing,
    lastResult,
    enqueue,
    remove,
    flush,
    refresh,
    start,
  }
})
