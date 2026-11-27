import { describe, expect, it } from 'vitest'
import {
  advanceProgress,
  matchesDifference,
  netCost,
  netCostText,
  reconciliationState,
} from './reconciliation'

const preview = (difference: number, toReconcileCount = 3) => ({
  date: '2026-11-20',
  statementBalance: 1000,
  clearedBalance: 1000 - difference,
  difference,
  toReconcileCount,
  unclearedCount: 1,
  lastDate: null,
})

describe('reconciliationState', () => {
  it('autorise le rapprochement sans écart', () => {
    const state = reconciliationState(preview(0))
    expect(state.canReconcile).toBe(true)
    expect(state.message).toContain('3 opération(s)')
  })

  it("refuse en cas d'écart, en l'indiquant", () => {
    const state = reconciliationState(preview(-12.5))
    expect(state.canReconcile).toBe(false)
    expect(state.message).toMatch(/^Écart de -12,50/)
  })

  it('refuse sans nouvelle opération pointée', () => {
    expect(reconciliationState(preview(0, 0)).canReconcile).toBe(false)
  })
})

describe('netCost', () => {
  it('déduit les remboursements d’une dépense', () => {
    expect(netCost({ amount: -50, reimbursedAmount: 30.1 })).toBe(-19.9)
    expect(netCostText({ amount: -50, reimbursedAmount: 30 })).toMatch(/^Remboursé 30,00.*coût net -20,00/)
  })

  it('ne dit rien d’une dépense non remboursée ou d’un revenu', () => {
    expect(netCost({ amount: -50, reimbursedAmount: 0 })).toBeNull()
    expect(netCost({ amount: 50, reimbursedAmount: 10 })).toBeNull()
  })
})

describe('advanceProgress', () => {
  it('borne la part reçue entre 0 et 100, une avance soldée étant à 100', () => {
    expect(advanceProgress({ expectedAmount: 200, received: 50, status: 'Open' })).toBe(25)
    expect(advanceProgress({ expectedAmount: 200, received: 250, status: 'Open' })).toBe(100)
    expect(advanceProgress({ expectedAmount: 200, received: 0, status: 'Settled' })).toBe(100)
  })
})

describe('matchesDifference', () => {
  it("repère l'opération dont le montant annule l'écart", () => {
    expect(matchesDifference(-12.5, -12.5)).toBe(true)
    expect(matchesDifference(0.1 + 0.2, 0.3)).toBe(true)
  })

  it('ignore les autres montants et un écart nul', () => {
    expect(matchesDifference(12.5, -12.5)).toBe(false)
    expect(matchesDifference(-12, -12.5)).toBe(false)
    expect(matchesDifference(0, 0)).toBe(false)
  })
})
