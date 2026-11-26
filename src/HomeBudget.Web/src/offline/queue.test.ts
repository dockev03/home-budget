import { describe, expect, it, vi } from 'vitest'
import { flushQueue, type QueuedTransaction, type QueueStore, type SyncSender } from './queue'
import { uuidv7 } from './uuid'

function memoryStore(items: QueuedTransaction[]): QueueStore & { items: Map<string, QueuedTransaction> } {
  const map = new Map(items.map((i) => [i.id, i]))
  return {
    items: map,
    all: async () => [...map.values()],
    put: async (item) => void map.set(item.id, item),
    remove: async (id) => void map.delete(id),
  }
}

function item(n: number, userId = 'u1'): QueuedTransaction {
  return {
    id: uuidv7(1_700_000_000_000 + n),
    userId,
    createdAt: '2026-11-26T10:00:00Z',
    status: 'pending',
    request: {
      accountId: 'a',
      date: '2026-11-26',
      payeeName: `Tiers ${n}`,
      label: null,
      notes: null,
      isCleared: null,
      lines: [{ categoryId: null, amount: -n, memo: null }],
    },
  }
}

describe('uuidv7', () => {
  it('produit un UUID version 7 triable par date', () => {
    const a = uuidv7(1_700_000_000_000)
    const b = uuidv7(1_700_000_000_001)
    expect(a).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(a < b).toBe(true)
    expect(a.slice(0, 13)).toBe('018bcfe5-6800')
  })
})

describe('flushQueue', () => {
  it('retire les opérations créées ou déjà reçues et garde les refus avec leur motif', async () => {
    const [one, two, three] = [item(1), item(2), item(3)]
    const store = memoryStore([one!, two!, three!])
    const send: SyncSender = vi.fn(async () => ({
      ok: true as const,
      results: [
        { id: one!.id, status: 'Created' as const, errors: null, transaction: null },
        { id: two!.id, status: 'AlreadyApplied' as const, errors: null, transaction: null },
        {
          id: three!.id,
          status: 'Rejected' as const,
          errors: { accountId: ['Ce compte est archivé.'] },
          transaction: null,
        },
      ],
    }))

    const result = await flushQueue(store, send, 'u1')

    expect(result).toEqual({ created: 1, rejected: 1, interrupted: false })
    expect([...store.items.values()]).toEqual([
      { ...three, status: 'rejected', error: 'Ce compte est archivé.' },
    ])
  })

  it('ne rejoue que les saisies de l’utilisateur connecté, dans l’ordre de saisie', async () => {
    const store = memoryStore([item(2), item(1), item(3, 'u2')])
    const send = vi.fn<SyncSender>(async (batch) => ({
      ok: true as const,
      results: batch.map((i) => ({ id: i.id, status: 'Created' as const, errors: null, transaction: null })),
    }))

    await flushQueue(store, send, 'u1')

    expect(send).toHaveBeenCalledTimes(1)
    expect(send.mock.calls[0]![0].map((i) => i.request.payeeName)).toEqual(['Tiers 1', 'Tiers 2'])
    expect([...store.items.values()].map((i) => i.userId)).toEqual(['u2'])
  })

  it('garde toute la file si le réseau ou le serveur est indisponible', async () => {
    const store = memoryStore([item(1), item(2)])
    const offline = await flushQueue(
      store,
      async () => Promise.reject(new TypeError('Failed to fetch')),
      'u1',
    )
    const down = await flushQueue(store, async () => ({ ok: false as const, status: 503 }), 'u1')

    expect(offline.interrupted).toBe(true)
    expect(down.interrupted).toBe(true)
    expect([...store.items.values()].every((i) => i.status === 'pending')).toBe(true)
  })

  it('isole un élément mal formé en renvoyant le lot un par un', async () => {
    const [good, bad] = [item(1), item(2)]
    const store = memoryStore([good!, bad!])
    const send = vi.fn<SyncSender>(async (batch) =>
      batch.some((i) => i.id === bad!.id)
        ? { ok: false as const, status: 400 }
        : {
            ok: true as const,
            results: [{ id: good!.id, status: 'Created' as const, errors: null, transaction: null }],
          },
    )

    const result = await flushQueue(store, send, 'u1')

    expect(send).toHaveBeenCalledTimes(3)
    expect(result).toEqual({ created: 1, rejected: 1, interrupted: false })
    expect(store.items.get(bad!.id)).toMatchObject({ status: 'rejected', error: 'Saisie invalide.' })
  })

  it('envoie par lots de 100', async () => {
    const store = memoryStore(Array.from({ length: 150 }, (_, n) => item(n + 1)))
    const send = vi.fn<SyncSender>(async (batch) => ({
      ok: true as const,
      results: batch.map((i) => ({ id: i.id, status: 'Created' as const, errors: null, transaction: null })),
    }))

    expect((await flushQueue(store, send, 'u1')).created).toBe(150)
    expect(send.mock.calls.map((c) => c[0].length)).toEqual([100, 50])
  })
})
