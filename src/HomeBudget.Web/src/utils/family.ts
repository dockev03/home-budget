import type { Schemas } from '@/api/client'
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
