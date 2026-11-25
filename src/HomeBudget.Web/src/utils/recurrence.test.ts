import { describe, expect, it } from 'vitest'
import { describeEnd, describeFrequency } from './recurrence'

describe('describeFrequency', () => {
  it('décrit une fréquence simple', () => {
    expect(describeFrequency('Day', 1)).toBe('Tous les jours')
    expect(describeFrequency('Week', 1)).toBe('Toutes les semaines')
    expect(describeFrequency('Month', 1)).toBe('Tous les mois')
    expect(describeFrequency('Year', 1)).toBe('Tous les ans')
  })

  it('décrit un intervalle', () => {
    expect(describeFrequency('Day', 3)).toBe('Tous les 3 jours')
    expect(describeFrequency('Week', 2)).toBe('Toutes les 2 semaines')
    expect(describeFrequency('Month', 3)).toBe('Tous les trimestres')
    expect(describeFrequency('Month', 6)).toBe('Tous les 6 mois')
    expect(describeFrequency('Year', 2)).toBe('Tous les 2 ans')
  })
})

describe('describeEnd', () => {
  it('décrit la fin', () => {
    expect(describeEnd('Never')).toBe('')
    expect(describeEnd('OnDate', '2026-12-31')).toBe('jusqu’au 31/12/2026')
    expect(describeEnd('AfterCount', null, 4)).toBe('4 échéances')
    expect(describeEnd('AfterCount', null, 1)).toBe('1 échéance')
  })
})
