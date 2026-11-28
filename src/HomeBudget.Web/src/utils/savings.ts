import type { Schemas } from '@/api/client'
import { formatAmount, formatDate } from './format'
import { formatRate } from './loans'

/**
 * Objectifs d'épargne (J1, J2, J4), épargne de précaution (J3), patrimoine (G5), valorisations (A7) et rémunération
 * des livrets (A9).
 */

export type SavingsGoal = Schemas['SavingsGoalDto']
export type SavingsGoalStatus = Schemas['SavingsGoalStatus']
export type NetWorthPoint = Schemas['NetWorthPointDto']
export type EmergencyFund = Schemas['EmergencyFundDto']
export type Valuation = Schemas['ValuationDto']
export type SavingsRate = Schemas['SavingsRateDto']
export type SavingsInterest = Schemas['SavingsInterestDto']
export type InterestFortnight = Schemas['InterestFortnightDto']

/** Durées proposées pour l'évolution du patrimoine, en périodes. */
export const NET_WORTH_LENGTHS = [12, 24, 60] as const
export type NetWorthLength = (typeof NET_WORTH_LENGTHS)[number]

export function isNetWorthLength(value: unknown): value is NetWorthLength {
  return NET_WORTH_LENGTHS.includes(Number(value) as NetWorthLength)
}

const statuses: Record<SavingsGoalStatus, { title: string; color: string }> = {
  InProgress: { title: 'En cours', color: 'primary' },
  Reached: { title: 'Atteint', color: 'success' },
  Overdue: { title: 'Date dépassée', color: 'warning' },
}

export const goalStatusTitle = (status: SavingsGoalStatus) => statuses[status].title
export const goalStatusColor = (status: SavingsGoalStatus) => statuses[status].color

/** Conseil de versement (J2) d'un objectif, en une phrase. */
export function contributionText(goal: SavingsGoal): string {
  if (goal.status === 'Reached') return 'Objectif atteint.'
  if (goal.status === 'Overdue') return `Date cible dépassée : il manque ${formatAmount(goal.remaining)}.`
  if (goal.monthlyContribution == null || goal.remainingPeriods == null) {
    return `Reste ${formatAmount(goal.remaining)} à épargner, sans date cible.`
  }
  return `Versement conseillé : ${formatAmount(goal.monthlyContribution)} par mois pendant ${goal.remainingPeriods} mois.`
}

/** Ce qui mesure l'épargne d'un objectif : un compte, ou une catégorie depuis une date. */
export function goalSource(
  goal: Pick<SavingsGoal, 'accountId' | 'categoryId' | 'startDate' | 'targetDate'>,
  accountName: (id: string) => string,
  categoryName: (id: string) => string,
): string {
  const source = goal.accountId
    ? `Solde de ${accountName(goal.accountId)}`
    : `${categoryName(goal.categoryId ?? '')} depuis le ${formatDate(goal.startDate)}`
  return goal.targetDate ? `${source} · pour le ${formatDate(goal.targetDate)}` : source
}

const oneDecimal = new Intl.NumberFormat('fr-FR', { maximumFractionDigits: 1 })

/** Épargne de précaution (J3) : « 2,4 mois de dépenses couverts ». */
export function monthsCoveredText(fund: Pick<EmergencyFund, 'monthsCovered'>): string {
  if (fund.monthsCovered == null) return "Pas encore assez d'historique de dépenses pour l'estimer."
  const months = fund.monthsCovered
  return `${oneDecimal.format(months)} mois de dépenses couvert${months >= 2 ? 's' : ''}`
}

/** Couleur de l'indicateur : moins de 3 mois, 3 à 6 mois, 6 mois et plus. */
export function monthsCoveredColor(months: number | null | undefined): string {
  if (months == null) return 'grey'
  if (months < 3) return 'error'
  if (months < 6) return 'warning'
  return 'success'
}

/** Évolution du patrimoine entre le premier et le dernier point. */
export function netWorthChange(points: Pick<NetWorthPoint, 'total'>[]): number {
  if (points.length < 2) return 0
  return Math.round((points.at(-1)!.total - points[0]!.total) * 100) / 100
}

const shortMonth = new Intl.DateTimeFormat('fr-FR', { month: 'short' })

/** Quinzaine lisible : « 1–15 janv. », « 16–28 févr. ». */
export function fortnightLabel(f: Pick<InterestFortnight, 'start' | 'end'>): string {
  const [y, m, startDay] = f.start.split('-').map(Number)
  const endDay = Number(f.end.split('-')[2])
  return `${startDay}–${endDay} ${shortMonth.format(new Date(y!, m! - 1, 1))}`
}

/** Taux du jour et prochain changement : « 1,5 %, puis 1,7 % à partir du 01/08/2026 ». */
export function rateText(interest: Pick<SavingsInterest, 'currentRate' | 'nextRate'>): string {
  const current = interest.currentRate == null ? 'Aucun taux en vigueur' : formatRate(interest.currentRate)
  const next = interest.nextRate
  return next
    ? `${current}, puis ${formatRate(next.annualRate)} à partir du ${formatDate(next.effectiveFrom)}`
    : current
}

/** Résumé d'un livret dans la liste des comptes : « 1,7 % · 12,34 € d'intérêts acquis cette année ». */
export function savingsSummary(account: {
  savingsRate?: number | null
  interestEarned?: number | null
}): string | null {
  if (account.savingsRate == null || account.interestEarned == null) return null
  return `${formatRate(account.savingsRate)} · ${formatAmount(account.interestEarned)} d'intérêts acquis cette année`
}
