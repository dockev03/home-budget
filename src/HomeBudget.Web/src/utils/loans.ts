import type { Schemas } from '@/api/client'
import { formatAmount, formatDate } from './format'

export type Loan = Schemas['LoanDto']
export type LoanInstallment = Schemas['LoanInstallmentDto']

/**
 * Lit un taux annuel saisi à la française : « 3,875 », « 3.5 % ». Renvoie null s'il n'est pas compris entre 0 et
 * 100 avec 4 décimales au plus.
 */
export function parseRate(text: string | number | null | undefined): number | null {
  if (typeof text === 'number') return Number.isFinite(text) && text >= 0 && text <= 100 ? text : null
  if (!text) return null
  const cleaned = text.replace(/[\s %]/g, '').replace(',', '.')
  if (!/^\d+(\.\d{1,4})?$/.test(cleaned)) return null
  const rate = Number(cleaned)
  return rate <= 100 ? rate : null
}

/** Taux au format français : « 3,875 % ». */
export function formatRate(rate: number): string {
  return `${rate.toLocaleString('fr-FR', { maximumFractionDigits: 4 })} %`
}

/** « 48 mensualités de 276,78 € (assurance comprise), jusqu'au 05/12/2029 ». */
export function loanSummary(loan: Loan): string {
  return `${loan.durationMonths} mensualités de ${formatAmount(loan.paymentWithInsurance)}${
    loan.monthlyInsurance > 0 ? ' (assurance comprise)' : ''
  }, jusqu'au ${formatDate(loan.endDate)}`
}

/** Part du capital déjà remboursée, de 0 à 100. */
export function repaidPercent(loan: Loan): number {
  if (loan.principal <= 0) return 0
  return Math.round(((loan.principal - loan.remainingPrincipal) / loan.principal) * 100)
}

/** Durée lisible : « 4 ans », « 2 ans et 3 mois », « 9 mois ». */
export function durationLabel(months: number): string {
  const years = Math.floor(months / 12)
  const rest = months % 12
  const y = years === 0 ? '' : years === 1 ? '1 an' : `${years} ans`
  const m = rest === 0 ? '' : `${rest} mois`
  return y && m ? `${y} et ${m}` : y || m
}
