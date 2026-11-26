import type { Schemas } from '@/api/client'
import { formatDate } from '@/utils/format'

export type FrequencyUnit = Schemas['FrequencyUnit']
export type RecurrenceEnd = Schemas['RecurrenceEnd']
export type RecurrenceMode = Schemas['RecurrenceMode']
export type BusinessDayShift = Schemas['BusinessDayShift']

export const frequencyUnits: { value: FrequencyUnit; title: string }[] = [
  { value: 'Day', title: 'jour(s)' },
  { value: 'Week', title: 'semaine(s)' },
  { value: 'Month', title: 'mois' },
  { value: 'Year', title: 'an(s)' },
]

export const recurrenceEnds: { value: RecurrenceEnd; title: string }[] = [
  { value: 'Never', title: 'Jamais' },
  { value: 'OnDate', title: 'À une date' },
  { value: 'AfterCount', title: 'Après un nombre d’échéances' },
]

export const recurrenceModes: { value: RecurrenceMode; title: string; hint: string }[] = [
  { value: 'Automatic', title: 'Automatique', hint: 'L’opération est créée le jour de l’échéance.' },
  { value: 'ToValidate', title: 'À valider', hint: 'L’échéance attend votre confirmation.' },
]

/** Report d'une échéance qui tombe un week-end ou un jour férié (E9). */
export const businessDayShifts: { value: BusinessDayShift; title: string }[] = [
  { value: 'None', title: 'Ne pas décaler' },
  { value: 'Next', title: 'Jour ouvré suivant' },
  { value: 'Previous', title: 'Jour ouvré précédent' },
]

/** Le report demande au moins 7 jours entre deux échéances (sinon deux échéances tomberaient le même jour). */
export function canShift(unit: FrequencyUnit, interval: number): boolean {
  return unit !== 'Day' || interval >= 7
}

const single: Record<FrequencyUnit, string> = {
  Day: 'Tous les jours',
  Week: 'Toutes les semaines',
  Month: 'Tous les mois',
  Year: 'Tous les ans',
}

const plural: Record<FrequencyUnit, (n: number) => string> = {
  Day: (n) => `Tous les ${n} jours`,
  Week: (n) => `Toutes les ${n} semaines`,
  Month: (n) => (n === 3 ? 'Tous les trimestres' : `Tous les ${n} mois`),
  Year: (n) => `Tous les ${n} ans`,
}

/** « Tous les mois », « Toutes les 2 semaines », « Tous les trimestres ». */
export function describeFrequency(unit: FrequencyUnit, interval: number): string {
  return interval <= 1 ? single[unit] : plural[unit](interval)
}

/** « jusqu'au 31/12/2026 », « 4 échéances », ou rien pour une récurrence sans fin. */
export function describeEnd(end: RecurrenceEnd, endDate?: string | null, count?: number | null): string {
  if (end === 'OnDate' && endDate) return `jusqu’au ${formatDate(endDate)}`
  if (end === 'AfterCount' && count) return count > 1 ? `${count} échéances` : '1 échéance'
  return ''
}
