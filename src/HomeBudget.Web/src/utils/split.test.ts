import { describe, expect, it } from 'vitest'
import { balanceColor, balanceText, sharesTotal, splitModeTitle, type SplitMember } from './split'

const member = (balance: number) => ({ balance }) as SplitMember

describe('répartition', () => {
  it('décrit le solde de chaque membre', () => {
    expect(balanceText(member(5))).toMatch(/^Le foyer lui doit 5,00\s€$/)
    expect(balanceText(member(-12.5))).toMatch(/^Doit 12,50\s€ au foyer$/)
    expect(balanceText(member(0))).toBe('À l’équilibre')
    expect(balanceColor(1)).toBe('success')
    expect(balanceColor(-1)).toBe('error')
    expect(balanceColor(0)).toBeUndefined()
  })

  it('totalise les parts au centime près', () => {
    expect(sharesTotal([33.33, 33.33, 33.34])).toBe(100)
    expect(sharesTotal([60, null])).toBe(60)
  })

  it('nomme les modes', () => {
    expect(splitModeTitle('IncomeProRata')).toBe('Au prorata des revenus')
  })
})
