import type { Schemas } from '@/api/client'
import { formatSignedAmount } from './format'

type Kind = Schemas['CategoryKind']

const percent = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 1, signDisplay: 'exceptZero' })

/** « +100,00 € (+25 %) », « -20,00 € », ou « = » sans écart. */
export function changeLabel(difference: number, changePercent: number | null): string {
  if (difference === 0) return '='
  const amount = formatSignedAmount(difference)
  return changePercent === null ? amount : `${amount} (${percent.format(changePercent)} %)`
}

/** Couleur d'un écart : une hausse est favorable pour un revenu ou l'épargne, défavorable pour une dépense. */
export function changeColor(kind: Kind, difference: number): string {
  if (difference === 0) return 'medium-emphasis'
  const favorable = kind === 'Income' || kind === 'Savings' ? difference > 0 : difference < 0
  return favorable ? 'success' : 'error'
}
