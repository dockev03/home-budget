import type { Schemas } from '@/api/client'
import { formatAmount, formatDate } from './format'

export type BudgetMode = Exclude<Schemas['BudgetMode'], null>
export type BudgetStatus = Schemas['BudgetStatus']
export type BudgetLine = Schemas['BudgetLineDto']

const monthYear = new Intl.DateTimeFormat('fr-FR', { month: 'long', year: 'numeric' })
const percent = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 2 })

/** Clé de période « 2026-11 » → « Novembre 2026 ». */
export function periodLabel(key: string): string {
  const [year, month] = key.split('-').map(Number)
  const text = monthYear.format(new Date(year!, month! - 1, 1))
  return text.charAt(0).toUpperCase() + text.slice(1)
}

/** « du 25/10/2026 au 24/11/2026 ». */
export function periodRange(start: string, end: string): string {
  return `du ${formatDate(start)} au ${formatDate(end)}`
}

export function formatPercent(value: number): string {
  return `${percent.format(value)} %`
}

export const budgetModes: { value: BudgetMode; title: string }[] = [
  { value: 'Amount', title: 'Montant' },
  { value: 'PercentOfIncome', title: '% des revenus' },
  { value: 'Auto', title: 'Automatique' },
]

/** Description courte du budget d'une ligne, sous son nom ; précise un budget propre à la période (D4). */
export function budgetModeLabel(line: BudgetLine): string {
  const base = baseModeLabel(line)
  const provision = line.provisioned > 0 ? ` + ${formatAmount(line.provisioned)} de provisions` : ''
  return `${base}${line.periodBudget ? ' · cette période' : ''}${provision}`
}

function baseModeLabel(line: BudgetLine): string {
  switch (line.mode) {
    case 'Amount':
      return 'Montant fixe'
    case 'PercentOfIncome':
      return `${formatPercent(line.periodBudget?.value ?? line.value ?? 0)} des revenus budgétés`
    case 'Auto': {
      const suffix = line.isDefaultMode ? ' (par défaut)' : ''
      return line.ownBudgeted === 0
        ? `Automatique${suffix} : aucune récurrence`
        : `Automatique${suffix} : récurrences`
    }
    default:
      return 'Aucun budget'
  }
}

/** Couleur Vuetify de la jauge (D9). */
export function statusColor(status: BudgetStatus): string {
  switch (status) {
    case 'Exceeded':
      return 'error'
    case 'Warning':
      return 'warning'
    case 'Ok':
      return 'success'
    default:
      return 'primary'
  }
}

/** « Reste 150,00 € », « Dépassé de 20,00 € », ou pour un revenu « Encore 100,00 € attendus ». */
export function remainingLabel(line: BudgetLine): string {
  if (line.kind === 'Income') {
    return line.remaining > 0
      ? `Encore ${formatAmount(line.remaining)} attendus`
      : `${formatAmount(-line.remaining)} de plus que prévu`
  }
  if (line.kind === 'Savings') {
    return line.remaining > 0 ? `Encore ${formatAmount(line.remaining)} à épargner` : 'Objectif atteint'
  }
  return line.remaining >= 0
    ? `Reste ${formatAmount(line.remaining)}`
    : `Dépassé de ${formatAmount(-line.remaining)}`
}

/** Remplissage de la jauge, borné à 100 %. */
export function gaugeValue(line: BudgetLine): number {
  if (line.percentUsed === null) return line.actual > 0 ? 100 : 0
  return Math.max(0, Math.min(100, line.percentUsed))
}
