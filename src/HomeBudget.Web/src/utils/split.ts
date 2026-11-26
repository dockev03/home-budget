import type { Schemas } from '@/api/client'
import { formatAmount } from './format'

export type SplitMode = Schemas['SplitMode']
export type Split = Schemas['SplitDto']
export type SplitMember = Schemas['SplitMemberDto']

export const splitModes: { value: SplitMode; title: string; hint: string }[] = [
  { value: 'Equal', title: 'À parts égales', hint: 'Chaque membre actif porte la même part.' },
  {
    value: 'IncomeProRata',
    title: 'Au prorata des revenus',
    hint: 'Selon les revenus de la période sur les comptes partagés dont chacun est titulaire.',
  },
  { value: 'FixedShares', title: 'Parts fixes', hint: 'Selon les pourcentages saisis (100 % au total).' },
]

export const splitModeTitle = (mode: SplitMode) => splitModes.find((m) => m.value === mode)?.title ?? mode

/** Solde d'un membre : « Le foyer lui doit 5,00 € », « Doit 5,00 € au foyer » ou « À l'équilibre ». */
export function balanceText(member: SplitMember): string {
  if (member.balance > 0) return `Le foyer lui doit ${formatAmount(member.balance)}`
  if (member.balance < 0) return `Doit ${formatAmount(-member.balance)} au foyer`
  return 'À l’équilibre'
}

export function balanceColor(balance: number): string | undefined {
  return balance > 0 ? 'success' : balance < 0 ? 'error' : undefined
}

/** Total des parts fixes saisies, au centime près. */
export function sharesTotal(percents: (number | null)[]): number {
  return Math.round(percents.reduce<number>((sum, p) => sum + (p ?? 0) * 100, 0)) / 100
}
