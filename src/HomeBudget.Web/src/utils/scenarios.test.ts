import { describe, expect, it } from 'vitest'
import { activeScenarioIds, describeWhen, isHorizon, keepKnown, type Scenario } from './scenarios'

const scenario = (id: string, isActive: boolean): Scenario => ({
  id,
  name: id,
  isActive,
  version: 1,
  events: [],
})

describe('describeWhen', () => {
  it('décrit un événement ponctuel', () => {
    expect(describeWhen({ frequency: 'Once', date: '2026-03-15', endDate: null })).toBe(
      'Une fois, le 15/03/2026',
    )
  })

  it('décrit un événement mensuel, avec ou sans fin', () => {
    expect(describeWhen({ frequency: 'Monthly', date: '2026-04-01', endDate: null })).toBe(
      'Chaque mois dès le 01/04/2026',
    )
    expect(describeWhen({ frequency: 'Monthly', date: '2026-04-01', endDate: '2026-12-31' })).toBe(
      'Chaque mois dès le 01/04/2026, jusqu’au 31/12/2026',
    )
  })
})

describe('sélection des scénarios', () => {
  it('propose les scénarios actifs', () => {
    expect(activeScenarioIds([scenario('a', true), scenario('b', false)])).toEqual(['a'])
  })

  it('oublie les scénarios supprimés', () => {
    expect(keepKnown(['a', 'x'], [scenario('a', true)])).toEqual(['a'])
  })
})

describe('isHorizon', () => {
  it('accepte 1, 3, 6 et 12 périodes', () => {
    expect([1, 3, 6, 12].every(isHorizon)).toBe(true)
    expect(isHorizon(2)).toBe(false)
    expect(isHorizon('3')).toBe(false)
  })
})
