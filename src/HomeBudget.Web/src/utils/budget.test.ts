import { describe, expect, it } from 'vitest'
import {
  budgetModeLabel,
  canCarryOver,
  movableAmount,
  toAssignLabel,
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
    periodBudget: null,
    provisioned: 0,
    defaultMode: 'Amount',
    carriedOver: 0,
    carryOverSince: null,
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

  it('signale un budget propre à la période et les provisions', () => {
    expect(budgetModeLabel(line({ periodBudget: { mode: 'Amount', value: 700, version: 1 } }))).toBe(
      'Montant fixe · cette période',
    )
    expect(
      budgetModeLabel(
        line({
          mode: 'PercentOfIncome',
          value: 10,
          periodBudget: { mode: 'PercentOfIncome', value: 15, version: 1 },
        }),
      ),
    ).toBe(nbsp('15 % des revenus budgétés · cette période'))
    expect(nbsp(budgetModeLabel(line({ provisioned: 120 })))).toBe('Montant fixe + 120,00 € de provisions')
  })

  it('signale le reste reporté et ce qui peut être déplacé', () => {
    expect(nbsp(budgetModeLabel(line({ carriedOver: -50 })))).toBe(nbsp('Montant fixe · report -50,00 €'))
    expect(nbsp(budgetModeLabel(line({ carriedOver: 12.5 })))).toBe(nbsp('Montant fixe · report +12,50 €'))
    expect(movableAmount(line({ ownBudgeted: 650, provisioned: 100, carriedOver: -50 }))).toBe(600)
    expect(canCarryOver(line())).toBe(true)
    expect(canCarryOver(line({ kind: 'Savings' }))).toBe(false)
    expect(canCarryOver(line({ isArchived: true }))).toBe(false)
    expect(nbsp(toAssignLabel(120))).toBe(nbsp('120,00 € à affecter'))
    expect(nbsp(toAssignLabel(-30))).toBe(nbsp('30,00 € affectés en trop'))
  })
})
