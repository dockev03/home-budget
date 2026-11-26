import type { Schemas } from '@/api/client'
import { fromIso, toIso } from './format'

export type CalendarEntry = Schemas['CalendarEntryDto']
export type CalendarStatus = Schemas['CalendarEntryStatus']

export interface CalendarDay {
  date: string
  day: number
  /** Jour hors de la période affichée (début ou fin de semaine). */
  outside: boolean
  entries: CalendarEntry[]
}

export const WEEK_DAYS = ['lun.', 'mar.', 'mer.', 'jeu.', 'ven.', 'sam.', 'dim.']

/**
 * Grille du calendrier (E8) : semaines complètes du lundi au dimanche couvrant la période, chaque jour avec ses
 * échéances.
 */
export function calendarWeeks(start: string, end: string, entries: CalendarEntry[]): CalendarDay[][] {
  const byDate = new Map<string, CalendarEntry[]>()
  for (const entry of entries) {
    const list = byDate.get(entry.date) ?? []
    list.push(entry)
    byDate.set(entry.date, list)
  }
  const first = fromIso(start)
  first.setDate(first.getDate() - ((first.getDay() + 6) % 7))
  const last = fromIso(end)
  last.setDate(last.getDate() + ((7 - last.getDay()) % 7))

  const weeks: CalendarDay[][] = []
  for (const day = first; day <= last; day.setDate(day.getDate() + 1)) {
    if ((day.getDay() + 6) % 7 === 0) weeks.push([])
    const iso = toIso(day)
    weeks.at(-1)!.push({
      date: iso,
      day: day.getDate(),
      outside: iso < start || iso > end,
      entries: byDate.get(iso) ?? [],
    })
  }
  return weeks
}

export const calendarStatuses: Record<CalendarStatus, { title: string; color: string }> = {
  Upcoming: { title: 'À venir', color: 'primary' },
  Pending: { title: 'À valider', color: 'warning' },
  Generated: { title: 'Passée', color: 'success' },
  Matched: { title: 'Rapprochée', color: 'success' },
  Skipped: { title: 'Sautée', color: 'grey' },
}

/** Échéance à valider dont la date est passée : en retard. */
export function isLate(entry: CalendarEntry, today: string): boolean {
  return entry.status === 'Pending' && entry.date < today
}

/** Somme signée des échéances d'un jour, sautées et virements exclus. */
export function dayTotal(entries: CalendarEntry[]): number {
  return (
    Math.round(
      entries
        .filter((e) => e.status !== 'Skipped' && !e.toAccountId)
        .reduce((sum, e) => sum + e.amount * 100, 0),
    ) / 100
  )
}
