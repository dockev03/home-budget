import type { Schemas } from '@/api/client'

export type TransactionRequest = Schemas['TransactionRequest']
export type SyncItemResult = Schemas['SyncItemResult']

/** Saisie faite hors ligne, en attente d'envoi (L4). */
export interface QueuedTransaction {
  /** UUID v7 attribué à la saisie : l'identifiant de l'opération créée. */
  id: string
  /** Utilisateur qui a saisi : la file n'est rejouée que dans sa session. */
  userId: string
  createdAt: string
  request: TransactionRequest
  status: 'pending' | 'rejected'
  /** Motif du refus par le serveur, prêt à afficher. */
  error?: string
}

/** Stockage de la file : IndexedDB (Dexie) dans l'appli, en mémoire dans les tests. */
export interface QueueStore {
  all(): Promise<QueuedTransaction[]>
  put(item: QueuedTransaction): Promise<void>
  remove(id: string): Promise<void>
}

/** Envoi d'un lot à POST /api/sync. */
export type SyncSender = (
  items: QueuedTransaction[],
) => Promise<{ ok: true; results: SyncItemResult[] } | { ok: false; status: number }>

export interface FlushResult {
  created: number
  rejected: number
  /** Réseau ou serveur indisponible : la file reste en attente. */
  interrupted: boolean
}

export const MAX_BATCH = 100

function errorText(errors: SyncItemResult['errors']): string {
  const messages = Object.values(errors ?? {}).flat()
  return messages.length > 0 ? messages.join(' ') : 'Refusée par le serveur.'
}

/**
 * Rejoue la file : par lots de 100 ; une opération créée ou déjà reçue sort de la file, une opération refusée y
 * reste avec son motif pour que l'utilisateur la corrige ou la supprime. Si le serveur refuse tout le lot (élément
 * mal formé, 400), les éléments sont renvoyés un par un pour isoler le fautif. Une coupure (réseau, 5xx, 401)
 * interrompt le rejeu sans rien perdre.
 */
export async function flushQueue(store: QueueStore, send: SyncSender, userId: string): Promise<FlushResult> {
  const result: FlushResult = { created: 0, rejected: 0, interrupted: false }
  const pending = (await store.all())
    .filter((i) => i.userId === userId && i.status === 'pending')
    .sort((a, b) => a.id.localeCompare(b.id))

  async function apply(batch: QueuedTransaction[]): Promise<boolean> {
    let response: Awaited<ReturnType<SyncSender>>
    try {
      response = await send(batch)
    } catch {
      return false
    }
    if (!response.ok) {
      if (response.status === 400 && batch.length > 1) {
        for (const item of batch) if (!(await apply([item]))) return false
        return true
      }
      if (response.status === 400 || response.status === 403) {
        for (const item of batch) {
          await store.put({
            ...item,
            status: 'rejected',
            error:
              response.status === 403
                ? 'Saisie non autorisée pour ce compte utilisateur.'
                : 'Saisie invalide.',
          })
          result.rejected++
        }
        return true
      }
      return false
    }
    for (const r of response.results) {
      const item = batch.find((i) => i.id === r.id)
      if (!item) continue
      if (r.status === 'Rejected') {
        await store.put({ ...item, status: 'rejected', error: errorText(r.errors) })
        result.rejected++
      } else {
        await store.remove(item.id)
        if (r.status === 'Created') result.created++
      }
    }
    return true
  }

  for (let i = 0; i < pending.length; i += MAX_BATCH) {
    if (!(await apply(pending.slice(i, i + MAX_BATCH)))) {
      result.interrupted = true
      break
    }
  }
  return result
}
