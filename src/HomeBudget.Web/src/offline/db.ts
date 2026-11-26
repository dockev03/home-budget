import Dexie, { type EntityTable } from 'dexie'
import type { QueuedTransaction, QueueStore } from './queue'

/** Base IndexedDB de l'appareil : la file d'attente des saisies hors ligne. */
class OfflineDatabase extends Dexie {
  queue!: EntityTable<QueuedTransaction, 'id'>

  constructor() {
    super('home-budget')
    this.version(1).stores({ queue: 'id, userId, status' })
  }
}

let database: OfflineDatabase | null = null

function db(): OfflineDatabase {
  database ??= new OfflineDatabase()
  return database
}

export const indexedDbStore: QueueStore = {
  all: () => db().queue.toArray(),
  put: async (item) => {
    await db().queue.put(item)
  },
  remove: (id) => db().queue.delete(id),
}
