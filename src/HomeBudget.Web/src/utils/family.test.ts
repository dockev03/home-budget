import { describe, expect, it } from 'vitest'
import {
  declarationForm,
  declarationRequest,
  declarationTotals,
  parseDeclarationValue,
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

  it('calcule les totaux comme le récapitulatif Pajemploi', () => {
    const zero = {
      mileageAllowance: 0,
      precariousnessAllowance: 0,
      paidLeaveCompensation: 0,
      noticeCompensation: 0,
      terminationAllowance: 0,
    }
    expect(
      declarationTotals({
        ...zero,
        netSalary: 638.25,
        maintenanceAllowance: 67.5,
        mealAllowance: 82.5,
        contributions: 539.53,
        coveredContributions: 539.53,
        cmgEntitlement: 446.4,
      }),
    ).toEqual({ remuneration: 788.25, totalCost: 1327.78, cmg: 985.93, remaining: 341.85 })
  })

  it('lit les nombres saisis : vide vaut 0, virgule acceptée, entiers pour les jours', () => {
    expect(parseDeclarationValue('')).toBe(0)
    expect(parseDeclarationValue('638,25 €')).toBe(638.25)
    expect(parseDeclarationValue('1,234')).toBeNull()
    expect(parseDeclarationValue('-3')).toBeNull()
    expect(parseDeclarationValue('16', true)).toBe(16)
    expect(parseDeclarationValue('16,5', true)).toBeNull()
  })

  it('construit la requête et ignore la fin de contrat si elle n’est pas cochée', () => {
    const form = declarationForm()
    expect(declarationRequest(form, 2026, 11)).toBeNull() // salarié manquant
    form.payeeId = 'p1'
    form.activityDays = '16'
    form.normalHours = '142'
    form.netSalary = '638,25'
    form.precariousnessAllowance = '10'
    form.contractEndDate = '2026-11-30'
    const request = declarationRequest(form, 2026, 11)!
    expect(request).toMatchObject({
      year: 2026,
      month: 11,
      payeeId: 'p1',
      activityDays: 16,
      normalHours: 142,
      netSalary: 638.25,
      netHourlyRate: null,
      precariousnessAllowance: 0,
      contractEndDate: null,
    })
    form.isContractEnd = true
    expect(declarationRequest(form, 2026, 11)).toMatchObject({
      precariousnessAllowance: 10,
      contractEndDate: '2026-11-30',
    })
    form.mealAllowance = 'abc'
    expect(declarationRequest(form, 2026, 11)).toBeNull()
  })

  it('remplit le formulaire depuis une déclaration existante', () => {
    const form = declarationForm({ payeeId: 'p1', netSalary: 638.25, normalHours: 142, advance: 0 } as never)
    expect(form).toMatchObject({ payeeId: 'p1', netSalary: '638,25', normalHours: '142', advance: '' })
  })
})
