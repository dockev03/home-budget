import { describe, expect, it } from 'vitest'
import { isRestoreConfirmed, transactionsExportUrl, type TransactionFilters } from './exports'

const empty: TransactionFilters = {
  accountId: null,
  categoryId: null,
  tagId: null,
  search: '',
  from: '',
  to: '',
  minAmount: '',
  maxAmount: '',
  cleared: null,
}

describe('transactionsExportUrl', () => {
  it('sans filtre, ne transmet que le format', () => {
    expect(transactionsExportUrl(empty, 'csv')).toBe('/api/exports/transactions?format=csv')
  })

  it('reprend les filtres de la recherche, montants saisis à la française compris', () => {
    const url = transactionsExportUrl(
      {
        accountId: 'acc',
        categoryId: 'cat',
        tagId: 'tag',
        search: ' cinéma & co ',
        from: '2026-01-01',
        to: '2026-01-31',
        minAmount: '1 000,50',
        maxAmount: 'abc',
        cleared: false,
      },
      'xlsx',
    )
    const query = new URL(url, 'https://localhost').searchParams
    expect(query.get('format')).toBe('xlsx')
    expect(query.get('accountId')).toBe('acc')
    expect(query.get('categoryId')).toBe('cat')
    expect(query.get('tagId')).toBe('tag')
    expect(query.get('search')).toBe('cinéma & co')
    expect(query.get('from')).toBe('2026-01-01')
    expect(query.get('to')).toBe('2026-01-31')
    expect(query.get('minAmount')).toBe('1000.5')
    expect(query.has('maxAmount')).toBe(false)
    expect(query.get('cleared')).toBe('false')
  })
})

describe('isRestoreConfirmed', () => {
  it('exige le mot exact', () => {
    expect(isRestoreConfirmed('RESTAURER')).toBe(true)
    expect(isRestoreConfirmed(' RESTAURER ')).toBe(true)
    expect(isRestoreConfirmed('restaurer')).toBe(false)
    expect(isRestoreConfirmed('')).toBe(false)
    expect(isRestoreConfirmed(null)).toBe(false)
  })
})
