import { parseAmount } from './format'

export type ExportFormat = 'csv' | 'xlsx'

/** Filtres de l'écran Opérations, tels que saisis. */
export interface TransactionFilters {
  accountId: string | null
  categoryId: string | null
  search: string
  from: string
  to: string
  minAmount: string
  maxAmount: string
  cleared: boolean | null
}

/** URL de l'export (H5) des opérations filtrées : mêmes filtres que la recherche. */
export function transactionsExportUrl(filters: TransactionFilters, format: ExportFormat): string {
  const query = new URLSearchParams({ format })
  if (filters.accountId) query.set('accountId', filters.accountId)
  if (filters.categoryId) query.set('categoryId', filters.categoryId)
  if (filters.search.trim()) query.set('search', filters.search.trim())
  if (filters.from) query.set('from', filters.from)
  if (filters.to) query.set('to', filters.to)
  const min = parseAmount(filters.minAmount)
  if (min !== null) query.set('minAmount', String(min))
  const max = parseAmount(filters.maxAmount)
  if (max !== null) query.set('maxAmount', String(max))
  if (filters.cleared !== null) query.set('cleared', String(filters.cleared))
  return `/api/exports/transactions?${query.toString()}`
}

/** Confirmation de la restauration : le mot exact, en majuscules. */
export const RESTORE_CONFIRMATION = 'RESTAURER'

export function isRestoreConfirmed(text: string | null | undefined): boolean {
  return (text ?? '').trim() === RESTORE_CONFIRMATION
}
