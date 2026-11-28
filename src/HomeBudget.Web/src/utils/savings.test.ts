import { describe, expect, it } from 'vitest'
import {
  contributionText,
  fortnightLabel,
  goalSource,
  isNetWorthLength,
  monthsCoveredColor,
  monthsCoveredText,
  netWorthChange,
  rateText,
  savingsSummary,
  type SavingsGoal,
  type SavingsRate,
} from './savings'

const goal = (overrides: Partial<SavingsGoal>): SavingsGoal => ({
  id: 'g',
  name: 'Vacances',
  targetAmount: 1500,
  targetDate: '2026-08-31',
  accountId: 'a',
  categoryId: null,
  startDate: '2026-03-10',
  color: null,
  icon: null,
  isArchived: false,
  saved: 500,
  remaining: 1000,
  percent: 33,
  status: 'InProgress',
  remainingPeriods: 6,
  monthlyContribution: 166.67,
  version: 1,
  ...overrides,
})

const nbsp = (text: string) => text.replace(/ | /g, ' ')

describe('contributionText', () => {
  it('conseille un versement mensuel', () => {
    expect(nbsp(contributionText(goal({})))).toBe('Versement conseillé : 166,67 € par mois pendant 6 mois.')
    expect(nbsp(contributionText(goal({ remainingPeriods: 1, monthlyContribution: 1000 })))).toBe(
      'Versement conseillé : 1 000,00 € par mois pendant 1 mois.',
    )
  })

  it('signale un objectif atteint, dépassé ou sans date', () => {
    expect(contributionText(goal({ status: 'Reached' }))).toBe('Objectif atteint.')
    expect(nbsp(contributionText(goal({ status: 'Overdue', monthlyContribution: null })))).toBe(
      'Date cible dépassée : il manque 1 000,00 €.',
    )
    expect(
      nbsp(contributionText(goal({ targetDate: null, remainingPeriods: null, monthlyContribution: null }))),
    ).toBe('Reste 1 000,00 € à épargner, sans date cible.')
  })
})

describe('goalSource', () => {
  const account = (id: string) => (id === 'a' ? 'Livret Maël' : '?')
  const category = (id: string) => (id === 'c' ? 'Épargne' : '?')

  it('décrit un compte ou une catégorie, avec la date cible', () => {
    expect(goalSource(goal({}), account, category)).toBe('Solde de Livret Maël · pour le 31/08/2026')
    expect(goalSource(goal({ accountId: null, categoryId: 'c', targetDate: null }), account, category)).toBe(
      'Épargne depuis le 10/03/2026',
    )
  })
})

describe('épargne de précaution', () => {
  it('donne le nombre de mois couverts', () => {
    expect(monthsCoveredText({ monthsCovered: 2.4 })).toBe('2,4 mois de dépenses couverts')
    expect(monthsCoveredText({ monthsCovered: 1 })).toBe('1 mois de dépenses couvert')
    expect(monthsCoveredText({ monthsCovered: null })).toContain('historique')
  })

  it('colore selon les seuils de 3 et 6 mois', () => {
    expect(monthsCoveredColor(null)).toBe('grey')
    expect(monthsCoveredColor(2.9)).toBe('error')
    expect(monthsCoveredColor(3)).toBe('warning')
    expect(monthsCoveredColor(6)).toBe('success')
  })
})

describe('patrimoine', () => {
  it('calcule l’évolution sur la durée affichée', () => {
    expect(netWorthChange([{ total: 1000 }, { total: 900.1 }, { total: 1250.3 }])).toBe(250.3)
    expect(netWorthChange([{ total: 1000 }])).toBe(0)
  })

  it('reconnaît les durées proposées', () => {
    expect(isNetWorthLength('24')).toBe(true)
    expect(isNetWorthLength(7)).toBe(false)
  })
})

describe('rémunération des livrets (A9)', () => {
  const rate = (effectiveFrom: string, annualRate: number): SavingsRate => ({
    id: 'r',
    accountId: 'a',
    effectiveFrom,
    annualRate,
    isScheduled: true,
    version: 1,
  })

  it('nomme une quinzaine par ses jours et son mois', () => {
    expect(fortnightLabel({ start: '2026-01-01', end: '2026-01-15' })).toBe('1–15 janv.')
    expect(fortnightLabel({ start: '2028-02-16', end: '2028-02-29' })).toBe('16–29 févr.')
  })

  it('annonce le taux du jour et le prochain changement', () => {
    expect(rateText({ currentRate: 1.5, nextRate: rate('2026-08-01', 1.7) })).toBe(
      '1,5 %, puis 1,7 % à partir du 01/08/2026',
    )
    expect(rateText({ currentRate: 1.7, nextRate: null })).toBe('1,7 %')
    expect(rateText({ currentRate: null, nextRate: null })).toBe('Aucun taux en vigueur')
  })

  it('résume un livret dans la liste des comptes, seulement s’il a un taux', () => {
    expect(savingsSummary({ savingsRate: 1.7, interestEarned: 12.34 })).toMatch(
      /^1,7 % · 12,34\s€ d'intérêts acquis cette année$/,
    )
    expect(savingsSummary({ savingsRate: null, interestEarned: null })).toBeNull()
  })
})
