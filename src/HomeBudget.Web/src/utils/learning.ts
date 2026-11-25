import type { Schemas } from '@/api/client'

type Transaction = Schemas['TransactionDto']

/** Nombre de raccourcis « refaire une opération récente » proposés à la saisie (L5). */
export const maxShortcuts = 6

/**
 * Raccourcis de saisie (L5) : les opérations récentes, une seule par tiers (ou libellé) et catégorie,
 * de la plus récente à la plus ancienne. Les virements et les opérations sans tiers ni libellé sont écartés.
 */
export function recentShortcuts(transactions: Transaction[], max = maxShortcuts): Transaction[] {
  const seen = new Set<string>()
  const result: Transaction[] = []
  for (const t of transactions) {
    const name = (t.payeeName ?? t.label ?? '').trim().toLowerCase()
    if (t.transferId || !name) continue
    const key = `${name}|${t.lines.length > 1 ? 'split' : (t.lines[0]?.categoryId ?? '')}`
    if (seen.has(key)) continue
    seen.add(key)
    result.push(t)
    if (result.length >= max) break
  }
  return result
}

/** Tiers et catégorie unique d'une opération : ce qu'une correction peut propager (I4). */
export interface Correction {
  payeeName: string | null
  categoryId: string | null
}

export function correctionOf(t: Pick<Transaction, 'payeeName' | 'lines'>): Correction {
  return {
    payeeName: t.payeeName?.trim() || null,
    categoryId: t.lines.length === 1 ? (t.lines[0]?.categoryId ?? null) : null,
  }
}

/**
 * La modification a-t-elle changé le tiers ou la catégorie ? Seul un tiers ou une catégorie renseignés comptent :
 * les retirer n'est pas une correction à propager.
 */
export function isCorrection(before: Correction, after: Correction): boolean {
  const payeeChanged =
    !!after.payeeName && after.payeeName.toLowerCase() !== (before.payeeName ?? '').toLowerCase()
  const categoryChanged = !!after.categoryId && after.categoryId !== before.categoryId
  return payeeChanged || categoryChanged
}

/** Message de fin d'une modification en masse. */
export function bulkMessage(result: Schemas['BulkUpdateResult']): string {
  const updated = `${result.updated} opération${result.updated > 1 ? 's' : ''} modifiée${result.updated > 1 ? 's' : ''}`
  if (!result.skipped) return `${updated}.`
  return `${updated} ; ${result.skipped} ignorée${result.skipped > 1 ? 's' : ''} (virement, opération ventilée ou importée, compte incompatible).`
}

/** Tags saisis dans un champ à puces : espaces superflus retirés, sans doublon (casse ignorée), sans vide. */
export function cleanTags(tags: readonly string[] | null | undefined): string[] {
  const seen = new Set<string>()
  const result: string[] = []
  for (const raw of tags ?? []) {
    const tag = raw.replace(/\s+/g, ' ').trim()
    const key = tag.toLocaleUpperCase('fr')
    if (!tag || seen.has(key)) continue
    seen.add(key)
    result.push(tag)
  }
  return result
}
