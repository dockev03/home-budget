import { describe, expect, it } from 'vitest'
import {
  budgetModeLabel,
  gaugeValue,
  periodLabel,
  periodRange,
  remainingLabel,
  statusColor,
  type BudgetLine,
} from './budget'

const nbsp = (text: string) => text.replace(/[  ]/g, ' ')

function line(overrides: Partial<BudgetLine> = {}): BudgetLine {
  return {
    categoryId: 'c',
    parentId: null,
    name: 'Courses',
    kind: 'Variable',
    color: null,
    icon: null,
    isArchived: false,
    mode: 'Amount',
    isDefaultMode: false,
    value: 600,
    budgetVersion: 1,
    ownBudgeted: 600,
    budgeted: 600,
    actual: 450,
    remaining: 150,
    percentUsed: 75,
    status: 'Ok',
    ...overrides,
  }
}

describe('budget', () => {
  it('nomme la période par son mois', () => {
    expect(periodLabel('2026-11')).toBe('Novembre 2026')
    expect(periodLabel('2027-01')).toBe('Janvier 2027')
    expect(periodRange('2026-10-25', '2026-11-24')).toBe('du 25/10/2026 au 24/11/2026')
  })

  it('décrit le mode de budget', () => {
    expect(budgetModeLabel(line())).toBe('Montant fixe')
    expect(nbsp(budgetModeLabel(line({ mode: 'PercentOfIncome', value: 12.5 })))).toBe(
      '12,5 % des revenus budgétés',
    )
    expect(budgetModeLabel(line({ mode: 'Auto', isDefaultMode: true, ownBudgeted: 0 }))).toBe(
      'Automatique (par défaut) : aucune récurrence',
    )
    expect(budgetModeLabel(line({ mode: null }))).toBe('Aucun budget')
  })

  it('indique le reste ou le dépassement', () => {
    expect(nbsp(remainingLabel(line()))).toBe('Reste 150,00 €')
    expect(nbsp(remainingLabel(line({ remaining: -20 })))).toBe('Dépassé de 20,00 €')
    expect(nbsp(remainingLabel(line({ kind: 'Income', remaining: 100 })))).toBe('Encore 100,00 € attendus')
  })

  it('borne la jauge et choisit sa couleur', () => {
    expect(gaugeValue(line({ percentUsed: 130 }))).toBe(100)
    expect(gaugeValue(line({ percentUsed: null, actual: 10 }))).toBe(100)
    expect(gaugeValue(line({ percentUsed: null, actual: 0 }))).toBe(0)
    expect(statusColor('Warning')).toBe('warning')
    expect(statusColor('Exceeded')).toBe('error')
  })
})
