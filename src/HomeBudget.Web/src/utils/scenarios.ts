import type { Schemas } from '@/api/client'
import { formatDate } from './format'

/** Scénarios « et si… » (F5) et horizon de la prévision (F4) : fonctions pures. */

export type Scenario = Schemas['ScenarioDto']
export type ScenarioEvent = Schemas['ScenarioEventDto']

/** Horizons proposés, en nombre de périodes, la période en cours comprise. */
export const HORIZONS = [
  { value: 1, title: 'Période' },
  { value: 3, title: '3 mois' },
  { value: 6, title: '6 mois' },
  { value: 12, title: '12 mois' },
] as const

export function isHorizon(value: unknown): value is number {
  return HORIZONS.some((h) => h.value === value)
}

/** « Une fois, le 15/03/2026 » ; « Chaque mois dès le 01/04/2026, jusqu’au 31/12/2026 ». */
export function describeWhen(e: Pick<ScenarioEvent, 'frequency' | 'date' | 'endDate'>): string {
  if (e.frequency === 'Once') return `Une fois, le ${formatDate(e.date)}`
  const until = e.endDate ? `, jusqu’au ${formatDate(e.endDate)}` : ''
  return `Chaque mois dès le ${formatDate(e.date)}${until}`
}

/** Scénarios actifs : sélectionnés par défaut sur la courbe du tableau de bord. */
export function activeScenarioIds(scenarios: Scenario[]): string[] {
  return scenarios.filter((s) => s.isActive).map((s) => s.id)
}

/** Ne garde que les identifiants de scénarios qui existent encore. */
export function keepKnown(ids: string[], scenarios: Scenario[]): string[] {
  const known = new Set(scenarios.map((s) => s.id))
  return ids.filter((id) => known.has(id))
}
