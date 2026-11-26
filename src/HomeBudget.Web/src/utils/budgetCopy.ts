import type { Schemas } from '@/api/client'
import { formatPercent } from './budget'
import { formatAmount } from './format'

type Mode = Schemas['BudgetMode'] | null

/** Budget décrit en une ligne : « 450,00 € », « 10 % des revenus », « Automatique (30,00 €) », « Aucun budget ». */
export function describeDefinition(mode: Mode, value: number | null, budgeted: number | null): string {
  switch (mode) {
    case 'Amount':
      return formatAmount(value ?? 0)
    case 'PercentOfIncome':
      return `${formatPercent(value ?? 0)} des revenus`
    case 'Auto':
      return budgeted === null ? 'Automatique' : `Automatique (${formatAmount(budgeted)})`
    default:
      return 'Aucun budget'
  }
}
