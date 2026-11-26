import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, type Schemas } from '@/api/client'
import { unwrap } from '@/api/errors'

export type Account = Schemas['AccountDto']
export type Category = Schemas['CategoryDto']

/** Données de référence partagées par les écrans : comptes (avec soldes) et catégories. */
export const useReferenceStore = defineStore('reference', () => {
  const accounts = ref<Account[]>([])
  const netWorth = ref(0)
  const categories = ref<Category[]>([])
  /** Noms des utilisateurs, pour afficher l'auteur d'une opération (K2). */
  const userNames = ref(new Map<string, string>())
  const loaded = ref(false)

  async function loadAccounts() {
    const list = await unwrap(api.GET('/api/accounts', { params: { query: { includeArchived: true } } }))
    accounts.value = list.items
    netWorth.value = list.netWorth
  }

  async function loadCategories() {
    categories.value = await unwrap(
      api.GET('/api/categories', { params: { query: { includeArchived: true } } }),
    )
  }

  async function loadUsers() {
    const users = await unwrap(api.GET('/api/users/directory'))
    userNames.value = new Map(users.map((u) => [u.id, u.name]))
  }

  async function load(force = false) {
    if (loaded.value && !force) return
    await Promise.all([loadAccounts(), loadCategories(), loadUsers()])
    loaded.value = true
  }

  const activeAccounts = computed(() => accounts.value.filter((a) => !a.isArchived))
  const accountById = computed(() => new Map(accounts.value.map((a) => [a.id, a])))
  const categoryById = computed(() => new Map(categories.value.map((c) => [c.id, c])))

  /** « Courses › Drive » pour une sous-catégorie. */
  function categoryLabel(id: string | null | undefined): string {
    if (!id) return 'Sans catégorie'
    const category = categoryById.value.get(id)
    if (!category) return 'Catégorie inconnue'
    const parent = category.parentId ? categoryById.value.get(category.parentId) : undefined
    return parent ? `${parent.name} › ${category.name}` : category.name
  }

  function reset() {
    accounts.value = []
    categories.value = []
    netWorth.value = 0
    userNames.value = new Map()
    loaded.value = false
  }

  return {
    accounts,
    userNames,
    loadUsers,
    netWorth,
    categories,
    activeAccounts,
    accountById,
    categoryById,
    load,
    loadAccounts,
    loadCategories,
    categoryLabel,
    reset,
  }
})
