import { describe, expect, it } from 'vitest'
import { durationLabel, formatRate, loanSummary, parseRate, repaidPercent, type Loan } from './loans'

const loan = {
  principal: 12000,
  remainingPrincipal: 9000,
  durationMonths: 48,
  paymentWithInsurance: 276.78,
  monthlyInsurance: 6.5,
  endDate: '2029-12-05',
} as Loan

describe('parseRate', () => {
  it('lit un taux à la française, 4 décimales au plus', () => {
    expect(parseRate('3,875')).toBe(3.875)
    expect(parseRate(' 3.5 % ')).toBe(3.5)
    expect(parseRate('0')).toBe(0)
    expect(parseRate(2)).toBe(2)
  })

  it('refuse un taux négatif, supérieur à 100 % ou trop précis', () => {
    expect(parseRate('-1')).toBeNull()
    expect(parseRate('101')).toBeNull()
    expect(parseRate('3,12345')).toBeNull()
    expect(parseRate('')).toBeNull()
    expect(parseRate('abc')).toBeNull()
  })
})

describe('affichage du crédit', () => {
  it('formate le taux, la synthèse et la durée', () => {
    expect(formatRate(3.875)).toBe('3,875 %')
    expect(loanSummary(loan)).toMatch(
      /^48 mensualités de 276,78\s€ \(assurance comprise\), jusqu'au 05\/12\/2029$/,
    )
    expect(loanSummary({ ...loan, monthlyInsurance: 0 })).not.toContain('assurance')
    expect(durationLabel(48)).toBe('4 ans')
    expect(durationLabel(27)).toBe('2 ans et 3 mois')
    expect(durationLabel(9)).toBe('9 mois')
    expect(durationLabel(12)).toBe('1 an')
  })

  it('calcule la part du capital remboursée', () => {
    expect(repaidPercent(loan)).toBe(25)
    expect(repaidPercent({ ...loan, principal: 0 })).toBe(0)
  })
})
