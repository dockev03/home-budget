import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, type Schemas } from '@/api/client'
import { unwrap } from '@/api/errors'

/** Échéances « à valider » (E5) : la liste, et leur nombre affiché en badge dans la navigation. */
export const usePendingStore = defineStore('pending', () => {
  const items = ref<Schemas['PendingOccurrenceDto'][]>([])
  const loading = ref(false)

  async function load() {
    loading.value = true
    try {
      items.value = await unwrap(api.GET('/api/recurrences/pending'))
    } finally {
      loading.value = false
    }
  }

  const count = computed(() => items.value.length)

  function reset() {
    items.value = []
  }

  return { items, loading, count, load, reset }
})
