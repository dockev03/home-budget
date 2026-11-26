import type { Schemas } from '@/api/client'
import { formatAmount, formatSignedAmount } from './format'

type Preview = Schemas['ReconciliationPreviewDto']
type Transaction = Schemas['TransactionDto']
type Advance = Schemas['AdvanceDto']

/** Rapprochement (A5) : possible seulement sans écart, avec au moins une opération pointée à rapprocher. */
export function reconciliationState(preview: Preview): { canReconcile: boolean; message: string } {
  if (preview.difference !== 0) {
    return {
      canReconcile: false,
      message: `Écart de ${formatSignedAmount(preview.difference)} : pointez les opérations du relevé ou corrigez le solde saisi.`,
    }
  }
  if (preview.toReconcileCount === 0) {
    return { canReconcile: false, message: 'Aucune nouvelle opération pointée à rapprocher.' }
  }
  return {
    canReconcile: true,
    message: `Aucun écart : ${preview.toReconcileCount} opération(s) pointée(s) seront rapprochées et verrouillées.`,
  }
}

/** Coût net d'une dépense remboursée (B11), ou null si rien n'a été remboursé. */
export function netCost(t: Pick<Transaction, 'amount' | 'reimbursedAmount'>): number | null {
  if (t.amount >= 0 || !t.reimbursedAmount) return null
  return Math.round((t.amount + t.reimbursedAmount) * 100) / 100
}

/** Texte « Remboursé 30,00 € · coût net -20,00 € » d'une dépense remboursée. */
export function netCostText(t: Pick<Transaction, 'amount' | 'reimbursedAmount'>): string | null {
  const net = netCost(t)
  return net === null
    ? null
    : `Remboursé ${formatAmount(t.reimbursedAmount)} · coût net ${formatSignedAmount(net)}`
}

/** Part reçue d'une avance (B12), en pourcentage borné à 100. */
export function advanceProgress(a: Pick<Advance, 'expectedAmount' | 'received' | 'status'>): number {
  if (a.status === 'Settled') return 100
  if (a.expectedAmount <= 0) return 0
  return Math.min(100, Math.max(0, Math.round((a.received / a.expectedAmount) * 100)))
}
