import type { Schemas } from '@/api/client'

/** Comparaison avec une autre période (G6) : écarts en euros et en pourcentage. */

export type Comparison = Schemas['ComparisonDto']
export type ComparedWith = 'previous' | 'lastYear'

export interface ComparisonRow {
  key: string
  label: string
  kind: Schemas['CategoryKind']
  current: number
  reference: number
  /** Écart en euros : période − référence. */
  delta: number
  /** Écart en % de la référence (null si la référence est nulle). */
  percent: number | null
  /** L'écart va dans le bon sens : plus de revenus ou d'épargne, moins de dépenses. */
  favorable: boolean | null
}

const KINDS: {
  kind: Schemas['CategoryKind']
  label: string
  field: 'income' | 'fixed' | 'variable' | 'savings'
}[] = [
  { kind: 'Income', label: 'Revenus', field: 'income' },
  { kind: 'Fixed', label: 'Dépenses fixes', field: 'fixed' },
  { kind: 'Variable', label: 'Dépenses variables', field: 'variable' },
  { kind: 'Savings', label: 'Épargne', field: 'savings' },
]

function row(
  key: string,
  label: string,
  kind: Schemas['CategoryKind'],
  current: number,
  reference: number,
): ComparisonRow {
  const delta = Math.round((current - reference) * 100) / 100
  const percent = reference === 0 ? null : Math.round((delta / Math.abs(reference)) * 1000) / 10
  const isExpense = kind === 'Fixed' || kind === 'Variable'
  return {
    key,
    label,
    kind,
    current,
    reference,
    delta,
    percent,
    favorable: delta === 0 ? null : isExpense ? delta < 0 : delta > 0,
  }
}

/** Totaux par nature, dans l'ordre du récapitulatif (D8). */
export function kindRows(comparison: Comparison, against: ComparedWith): ComparisonRow[] {
  const reference = comparison[against]
  return KINDS.map((k) => row(k.kind, k.label, k.kind, comparison.current[k.field], reference[k.field]))
}

/** Catégories principales, les plus grands écarts d'abord. */
export function categoryRows(comparison: Comparison, against: ComparedWith): ComparisonRow[] {
  return comparison.categories
    .map((c) => row(c.categoryId, c.name, c.kind, c.current, c[against]))
    .filter((r) => r.current !== 0 || r.reference !== 0)
    .sort((a, b) => Math.abs(b.delta) - Math.abs(a.delta))
}

/** « +12,5 % », « −3 % », « — » sans référence. */
export function formatPercent(percent: number | null): string {
  if (percent === null) return '—'
  const text = Math.abs(percent).toLocaleString('fr-FR', { maximumFractionDigits: 1 })
  return percent > 0 ? `+${text} %` : percent < 0 ? `−${text} %` : '0 %'
}
