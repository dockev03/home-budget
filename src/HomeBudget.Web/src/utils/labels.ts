import type { Schemas } from '@/api/client'

export type AccountType = Schemas['AccountType']
export type CategoryKind = Schemas['CategoryKind']

export const accountTypes: { value: AccountType; title: string; icon: string }[] = [
  { value: 'Checking', title: 'Compte courant', icon: 'wallet' },
  { value: 'Savings', title: 'Épargne', icon: 'piggy-bank' },
  { value: 'Investment', title: 'Investissement', icon: 'chart-line' },
  { value: 'Cash', title: 'Espèces', icon: 'cash' },
  { value: 'Credit', title: 'Crédit', icon: 'credit-card' },
]

export const categoryKinds: { value: CategoryKind; title: string }[] = [
  { value: 'Income', title: 'Revenus' },
  { value: 'Fixed', title: 'Dépenses fixes' },
  { value: 'Variable', title: 'Dépenses variables' },
  { value: 'Savings', title: 'Épargne' },
]

export const accountTypeTitle = (type: AccountType) =>
  accountTypes.find((t) => t.value === type)?.title ?? type
export const categoryKindTitle = (kind: CategoryKind) =>
  categoryKinds.find((k) => k.value === kind)?.title ?? kind
