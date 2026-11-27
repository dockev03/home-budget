import type { Schemas } from '@/api/client'
import { roundCents } from './format'
import type { CategoryKind } from './labels'

export type FamilyRole = Schemas['FamilyRole']
export type BenefitStatus = Schemas['BenefitStatus']
export type Reimburser = Schemas['Reimburser']
export type HealthRow = Schemas['HealthRowDto']

export const familyRoles: { value: FamilyRole; title: string; kinds: CategoryKind[] }[] = [
  { value: 'None', title: 'Aucun', kinds: ['Income', 'Fixed', 'Variable', 'Savings'] },
  { value: 'Benefit', title: 'Aide suivie (CAF…)', kinds: ['Income'] },
  { value: 'ChildcareAid', title: 'Aide à la garde (CMG)', kinds: ['Income'] },
  { value: 'Childcare', title: 'Frais de garde', kinds: ['Fixed', 'Variable'] },
  { value: 'Health', title: 'Santé', kinds: ['Fixed', 'Variable'] },
]

/** Rôles proposés pour un type de catégorie : les aides sont des revenus, la garde et la santé des dépenses. */
export const rolesFor = (kind: CategoryKind) => familyRoles.filter((r) => r.kinds.includes(kind))

/** Le rôle reste valable après un changement de type ; sinon il revient à « Aucun ». */
export const roleAfterKindChange = (role: FamilyRole, kind: CategoryKind): FamilyRole =>
  rolesFor(kind).some((r) => r.value === role) ? role : 'None'

export const benefitStatuses: Record<BenefitStatus, { title: string; color: string }> = {
  Received: { title: 'Reçue', color: 'success' },
  Pending: { title: 'À valider', color: 'warning' },
  Skipped: { title: 'Sautée', color: 'error' },
  Missing: { title: 'Opération supprimée', color: 'error' },
  Upcoming: { title: 'À venir', color: 'grey' },
  Unscheduled: { title: 'Hors échéancier', color: 'info' },
}

export const reimbursers: { value: Reimburser; title: string }[] = [
  { value: 'PrimaryInsurance', title: 'CPAM' },
  { value: 'Mutual', title: 'Mutuelle' },
]

export const reimburserTitle = (value: Reimburser) =>
  reimbursers.find((r) => r.value === value)?.title ?? 'Aucun'

/** Couleur d'un écart : un manque en rouge, un trop-perçu en bleu, rien si l'écart est nul ou inconnu. */
export function gapColor(gap: number | null | undefined): string | undefined {
  if (gap == null || gap === 0) return undefined
  return gap < 0 ? 'error' : 'info'
}

/** Dépenses de santé encore sans aucun remboursement. */
export const unreimbursed = (rows: HealthRow[]) =>
  rows.filter((r) => r.primaryInsurance + r.mutual + r.other === 0)

/** Nom du mois (1 à 12), avec une majuscule. */
export function monthName(month: number): string {
  const text = new Intl.DateTimeFormat('fr-FR', { month: 'long' }).format(new Date(2000, month - 1, 1))
  return text.charAt(0).toUpperCase() + text.slice(1)
}

export type ChildcareDeclaration = Schemas['ChildcareDeclarationDto']
export type ChildcareDeclarationRequest = Schemas['ChildcareDeclarationRequest']

/** Champs décimaux d'une déclaration Pajemploi (heures, jours de congés, montants), saisis en texte. */
export type DeclarationDecimalField =
  | 'normalHours'
  | 'extraHours'
  | 'overtimeHours'
  | 'paidLeaveDays'
  | 'netHourlyRate'
  | 'netSalary'
  | 'maintenanceAllowance'
  | 'mealAllowance'
  | 'mileageAllowance'
  | 'advance'
  | 'precariousnessAllowance'
  | 'paidLeaveCompensation'
  | 'noticeCompensation'
  | 'terminationAllowance'
  | 'contributions'
  | 'coveredContributions'
  | 'cmgEntitlement'

export type DeclarationIntegerField = 'activityDays' | 'longCareDays'

/** Libellés des champs, dans l'ordre et selon les blocs du récapitulatif Pajemploi. */
export const declarationSections: {
  title: string
  advanced?: boolean
  fields: { key: DeclarationDecimalField | DeclarationIntegerField; label: string; suffix?: string }[]
}[] = [
  {
    title: 'Travail à déclarer',
    fields: [
      { key: 'activityDays', label: "Jours d'activité", suffix: 'j' },
      { key: 'normalHours', label: 'Heures normales', suffix: 'h' },
      { key: 'extraHours', label: 'Heures complémentaires', suffix: 'h' },
      { key: 'overtimeHours', label: 'Heures majorées', suffix: 'h' },
      { key: 'paidLeaveDays', label: 'Jours de « congés payés » versés', suffix: 'j' },
    ],
  },
  {
    title: 'Rémunération salariale',
    fields: [
      { key: 'netHourlyRate', label: "Taux horaire net d'une heure normale", suffix: '€' },
      { key: 'netSalary', label: 'Salaire net pour le mois', suffix: '€' },
    ],
  },
  {
    title: 'Indemnités et compléments de salaire',
    fields: [
      { key: 'maintenanceAllowance', label: "Indemnités d'entretien", suffix: '€' },
      { key: 'mealAllowance', label: 'Frais de repas', suffix: '€' },
      { key: 'mileageAllowance', label: 'Indemnités kilométriques', suffix: '€' },
      { key: 'advance', label: 'Acompte versé (pour information)', suffix: '€' },
    ],
  },
  {
    title: 'Résultat de la déclaration',
    fields: [
      { key: 'contributions', label: 'Montant total des cotisations', suffix: '€' },
      { key: 'coveredContributions', label: 'Cotisations prises en charge (CAF ou MSA)', suffix: '€' },
      { key: 'cmgEntitlement', label: 'Droit CMG', suffix: '€' },
    ],
  },
  {
    title: 'Informations complémentaires',
    advanced: true,
    fields: [{ key: 'longCareDays', label: 'Jours de garde de plus de 24 h', suffix: 'j' }],
  },
  {
    title: 'Indemnités de fin de contrat',
    advanced: true,
    fields: [
      { key: 'precariousnessAllowance', label: 'Indemnité de précarité', suffix: '€' },
      { key: 'paidLeaveCompensation', label: 'Indemnité compensatrice de congés payés', suffix: '€' },
      { key: 'noticeCompensation', label: 'Indemnité compensatrice de préavis', suffix: '€' },
      { key: 'terminationAllowance', label: 'Indemnité de rupture', suffix: '€' },
    ],
  },
]

export const integerFields: DeclarationIntegerField[] = ['activityDays', 'longCareDays']
export const endOfContractFields: DeclarationDecimalField[] = [
  'precariousnessAllowance',
  'paidLeaveCompensation',
  'noticeCompensation',
  'terminationAllowance',
]

type DeclarationAmounts = Pick<
  ChildcareDeclarationRequest,
  | 'netSalary'
  | 'maintenanceAllowance'
  | 'mealAllowance'
  | 'mileageAllowance'
  | 'precariousnessAllowance'
  | 'paidLeaveCompensation'
  | 'noticeCompensation'
  | 'terminationAllowance'
  | 'contributions'
  | 'coveredContributions'
  | 'cmgEntitlement'
>

/**
 * Totaux d'une déclaration, comme sur le récapitulatif Pajemploi (et comme l'API) : rémunération déclarée,
 * coût total de l'emploi, complément du mode de garde (CMG) et montant restant à charge. L'acompte n'y entre pas.
 */
export function declarationTotals(d: DeclarationAmounts) {
  const remuneration = roundCents(
    d.netSalary +
      d.maintenanceAllowance +
      d.mealAllowance +
      d.mileageAllowance +
      d.precariousnessAllowance +
      d.paidLeaveCompensation +
      d.noticeCompensation +
      d.terminationAllowance,
  )
  const totalCost = roundCents(remuneration + d.contributions)
  const cmg = roundCents(d.coveredContributions + d.cmgEntitlement)
  return { remuneration, totalCost, cmg, remaining: roundCents(totalCost - cmg) }
}

/** Formulaire d'une déclaration : chaque nombre est saisi en texte (virgule acceptée). */
export type DeclarationForm = Record<DeclarationDecimalField | DeclarationIntegerField, string> & {
  payeeId: string | null
  disabledChildCare: boolean
  isContractEnd: boolean
  contractEndDate: string
}

const allFields = declarationSections.flatMap((s) => s.fields.map((f) => f.key))

/** Formulaire vide, ou rempli depuis une déclaration existante (ou la précédente, pour recopier). */
export function declarationForm(from?: ChildcareDeclaration | null): DeclarationForm {
  const form = {
    payeeId: from?.payeeId ?? null,
    disabledChildCare: from?.disabledChildCare ?? false,
    isContractEnd: from?.isContractEnd ?? false,
    contractEndDate: from?.contractEndDate ?? '',
  } as DeclarationForm
  for (const key of allFields) {
    const value = from?.[key]
    form[key] = value == null || value === 0 ? '' : String(value).replace('.', ',')
  }
  return form
}

/** Nombre saisi : vide vaut 0 (ou null pour le taux horaire) ; null si la saisie est invalide. */
export function parseDeclarationValue(text: string, integer = false): number | null {
  const cleaned = text.replace(/[\s  €]/g, '').replace(',', '.')
  if (cleaned === '') return 0
  if (integer ? !/^\d+$/.test(cleaned) : !/^\d+(\.\d{1,2})?$/.test(cleaned)) return null
  return Number(cleaned)
}

/** Corps de la requête ; null si un champ est invalide ou si le salarié manque. */
export function declarationRequest(
  form: DeclarationForm,
  year: number,
  month: number,
): ChildcareDeclarationRequest | null {
  if (!form.payeeId) return null
  const values = {} as Record<DeclarationDecimalField | DeclarationIntegerField, number>
  for (const key of allFields) {
    const value = parseDeclarationValue(form[key], integerFields.includes(key as DeclarationIntegerField))
    if (value === null) return null
    values[key] = value
  }
  if (!form.isContractEnd) {
    for (const key of endOfContractFields) values[key] = 0
  }
  return {
    ...values,
    year,
    month,
    payeeId: form.payeeId,
    netHourlyRate: form.netHourlyRate.trim() === '' ? null : values.netHourlyRate,
    disabledChildCare: form.disabledChildCare,
    isContractEnd: form.isContractEnd,
    contractEndDate: form.isContractEnd && form.contractEndDate ? form.contractEndDate : null,
  }
}
