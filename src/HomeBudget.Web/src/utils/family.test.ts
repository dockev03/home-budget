import { describe, expect, it } from 'vitest'
import {
  benefitStatuses,
  gapColor,
  monthName,
  reimburserTitle,
  roleAfterKindChange,
  rolesFor,
  unreimbursed,
  type HealthRow,
} from './family'

const row = (primaryInsurance: number, mutual: number, other: number) =>
  ({ primaryInsurance, mutual, other }) as HealthRow

describe('suivis du foyer', () => {
  it('propose les rôles selon le type de catégorie', () => {
    expect(rolesFor('Income').map((r) => r.value)).toEqual(['None', 'Benefit', 'ChildcareAid'])
    expect(rolesFor('Variable').map((r) => r.value)).toEqual(['None', 'Childcare', 'Health'])
    expect(rolesFor('Savings').map((r) => r.value)).toEqual(['None'])
  })

  it('retire un rôle devenu incompatible avec le type', () => {
    expect(roleAfterKindChange('Health', 'Fixed')).toBe('Health')
    expect(roleAfterKindChange('Health', 'Income')).toBe('None')
    expect(roleAfterKindChange('Benefit', 'Variable')).toBe('None')
  })

  it('colore les écarts', () => {
    expect(gapColor(-5)).toBe('error')
    expect(gapColor(10)).toBe('info')
    expect(gapColor(0)).toBeUndefined()
    expect(gapColor(null)).toBeUndefined()
  })

  it('libelle les statuts et les organismes', () => {
    expect(benefitStatuses.Pending.title).toBe('À valider')
    expect(reimburserTitle('PrimaryInsurance')).toBe('CPAM')
    expect(reimburserTitle('None')).toBe('Aucun')
    expect(monthName(2)).toBe('Février')
  })

  it('isole les dépenses de santé sans remboursement', () => {
    expect(unreimbursed([row(0, 0, 0), row(35, 0, 0), row(0, 0, 2)])).toHaveLength(1)
  })
})
