import { describe, expect, it } from 'vitest'
import type { Schemas } from '@/api/client'
import { bulkMessage, cleanTags, correctionOf, isCorrection, recentShortcuts } from './learning'

type Transaction = Schemas['TransactionDto']

let next = 0
const transaction = (
  overrides: Partial<Transaction> = {},
  categoryIds: (string | null)[] = ['cat'],
): Transaction => ({
  id: `t${++next}`,
  accountId: 'acc',
  date: '2026-11-20',
  payeeId: null,
  payeeName: 'Boulangerie',
  label: null,
  notes: null,
  amount: -5,
  isCleared: false,
  transferId: null,
  transferAccountId: null,
  lines: categoryIds.map((categoryId, i) => ({
    id: `l${next}-${i}`,
    categoryId,
    amount: -5,
    memo: null,
    tags: [],
  })),
  createdAt: '2026-11-20T10:00:00Z',
  updatedAt: '2026-11-20T10:00:00Z',
  version: 1,
  isReconciled: false,
  reimbursesId: null,
  reimbursedAmount: 0,
  advanceId: null,
  createdBy: null,
  updatedBy: null,
  attachmentCount: 0,
  ...overrides,
})

describe('recentShortcuts', () => {
  it('garde la plus récente par tiers et catégorie, sans virement ni opération anonyme', () => {
    const recent = transaction({ payeeName: 'Boulangerie' })
    const list = [
      recent,
      transaction({ payeeName: ' boulangerie ' }),
      transaction({ payeeName: 'Boulangerie' }, ['autre']),
      transaction({ payeeName: null, transferId: 'tr' }),
      transaction({ payeeName: null, label: null }),
      transaction({ payeeName: null, label: 'Retrait' }),
    ]

    const shortcuts = recentShortcuts(list)

    expect(shortcuts.map((t) => t.id)).toEqual([recent.id, list[2]!.id, list[5]!.id])
  })

  it('se limite au nombre demandé', () => {
    const list = ['A', 'B', 'C'].map((payeeName) => transaction({ payeeName }))
    expect(recentShortcuts(list, 2)).toHaveLength(2)
  })
})

describe('isCorrection', () => {
  it('repère un nouveau tiers ou une nouvelle catégorie, casse ignorée', () => {
    const before = correctionOf(transaction({ payeeName: 'PRLV SEPA MACIF' }, [null]))
    expect(isCorrection(before, { payeeName: 'Macif', categoryId: null })).toBe(true)
    expect(isCorrection(before, { payeeName: 'prlv sepa macif', categoryId: null })).toBe(false)
    expect(isCorrection(before, { payeeName: 'PRLV SEPA MACIF', categoryId: 'assurance' })).toBe(true)
  })

  it("retirer le tiers ou la catégorie n'est pas une correction à propager", () => {
    const before = { payeeName: 'Macif', categoryId: 'assurance' }
    expect(isCorrection(before, { payeeName: null, categoryId: null })).toBe(false)
  })

  it("ignore la catégorie d'une opération ventilée", () => {
    expect(correctionOf(transaction({}, ['a', 'b'])).categoryId).toBeNull()
  })
})

describe('bulkMessage', () => {
  it('accorde le pluriel et signale les opérations ignorées', () => {
    expect(bulkMessage({ updated: 1, skipped: 0 })).toBe('1 opération modifiée.')
    expect(bulkMessage({ updated: 3, skipped: 2 })).toMatch(/^3 opérations modifiées ; 2 ignorées/)
  })
})

describe('cleanTags', () => {
  it('retire les espaces superflus, les vides et les doublons', () => {
    expect(cleanTags(['  Vacances   été ', 'vacances été', '', 'Travaux'])).toEqual([
      'Vacances été',
      'Travaux',
    ])
    expect(cleanTags(null)).toEqual([])
  })
})
