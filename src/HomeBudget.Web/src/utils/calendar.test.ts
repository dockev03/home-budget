import { describe, expect, it } from 'vitest'
import { calendarWeeks, dayTotal, isLate, type CalendarEntry } from './calendar'

const entry = (
  date: string,
  amount: number,
  status: CalendarEntry['status'] = 'Upcoming',
  toAccountId: string | null = null,
) => ({ date, amount, status, toAccountId, label: 'X' }) as CalendarEntry

describe('calendarWeeks', () => {
  it('couvre la période en semaines complètes du lundi au dimanche', () => {
    // Mars 2026 : du dimanche 1er au mardi 31.
    const weeks = calendarWeeks('2026-03-01', '2026-03-31', [
      entry('2026-03-10', -20),
      entry('2026-03-10', -5),
    ])
    expect(weeks).toHaveLength(6)
    expect(weeks.every((w) => w.length === 7)).toBe(true)
    expect(weeks[0]![0]!.date).toBe('2026-02-23')
    expect(weeks[0]![0]!.outside).toBe(true)
    expect(weeks[0]![6]!.date).toBe('2026-03-01')
    expect(weeks[0]![6]!.outside).toBe(false)
    expect(weeks.at(-1)!.at(-1)!.date).toBe('2026-04-05')
    const tenth = weeks.flat().find((d) => d.date === '2026-03-10')!
    expect(tenth.entries).toHaveLength(2)
    expect(tenth.day).toBe(10)
  })

  it('ne rajoute pas de semaine quand la période commence un lundi et finit un dimanche', () => {
    expect(calendarWeeks('2026-06-01', '2026-06-28', [])).toHaveLength(4)
  })
})

describe('échéances du jour', () => {
  it('totalise sans les échéances sautées ni les virements', () => {
    expect(
      dayTotal([
        entry('d', -20.1),
        entry('d', -0.2),
        entry('d', 100, 'Skipped'),
        entry('d', 50, 'Upcoming', 'acc'),
      ]),
    ).toBe(-20.3)
  })

  it('signale une échéance à valider en retard', () => {
    expect(isLate(entry('2026-03-01', -1, 'Pending'), '2026-03-10')).toBe(true)
    expect(isLate(entry('2026-03-10', -1, 'Pending'), '2026-03-10')).toBe(false)
    expect(isLate(entry('2026-03-01', -1, 'Upcoming'), '2026-03-10')).toBe(false)
  })
})
