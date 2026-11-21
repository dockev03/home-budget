import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { Schemas } from '@/api/client'

export type Transaction = Schemas['TransactionDto']
export type EditorMode = 'create' | 'edit' | 'duplicate'

/**
 * Ouverture du formulaire de saisie depuis n'importe quel écran (bouton « + », liste des opérations).
 * Les écrans surveillent « revision » pour se recharger après un enregistrement.
 */
export const useTransactionEditor = defineStore('transactionEditor', () => {
  const visible = ref(false)
  const mode = ref<EditorMode>('create')
  const transaction = ref<Transaction | null>(null)
  const defaultAccountId = ref<string | null>(null)
  const revision = ref(0)

  function open(
    newMode: EditorMode = 'create',
    source: Transaction | null = null,
    accountId: string | null = null,
  ) {
    mode.value = newMode
    transaction.value = source
    defaultAccountId.value = accountId
    visible.value = true
  }

  function saved() {
    visible.value = false
    revision.value++
  }

  return { visible, mode, transaction, defaultAccountId, revision, open, saved }
})
