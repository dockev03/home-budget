/** Disposition de l'accueil : blocs, ordre et blocs masqués (fonctions pures). */

export const HOME_BLOCKS = [
  { id: 'keyFigures', title: 'Chiffres clés' },
  { id: 'forecast', title: 'Prévision et courbe du solde' },
  { id: 'charts', title: 'Dépenses par catégorie, budget et réel' },
  { id: 'comparison', title: 'Comparaison' },
  { id: 'history', title: 'Historique sur 12 périodes' },
  { id: 'topPayees', title: 'Top des tiers' },
  { id: 'wealth', title: 'Patrimoine et épargne' },
  { id: 'accounts', title: 'Comptes' },
] as const

export type HomeBlockId = (typeof HOME_BLOCKS)[number]['id']

export interface HomeLayout {
  order: HomeBlockId[]
  hidden: HomeBlockId[]
}

export const DEFAULT_ORDER: HomeBlockId[] = HOME_BLOCKS.map((b) => b.id)

const known = new Set<string>(DEFAULT_ORDER)
const isKnown = (id: string): id is HomeBlockId => known.has(id)

export function blockTitle(id: HomeBlockId): string {
  return HOME_BLOCKS.find((b) => b.id === id)!.title
}

/**
 * Ramène une disposition reçue (serveur, cache hors ligne d'une autre version) aux blocs connus : sans doublon,
 * blocs manquants à la fin, blocs masqués dans l'ordre d'affichage. Même règle que le serveur.
 */
export function normalizeLayout(
  order?: readonly string[] | null,
  hidden?: readonly string[] | null,
): HomeLayout {
  const result = [...new Set((order ?? []).filter(isKnown))]
  result.push(...DEFAULT_ORDER.filter((id) => !result.includes(id)))
  const masked = new Set((hidden ?? []).filter(isKnown))
  return { order: result, hidden: result.filter((id) => masked.has(id)) }
}

/** Blocs à afficher, dans l'ordre. */
export function visibleBlocks(layout: HomeLayout): HomeBlockId[] {
  return layout.order.filter((id) => !layout.hidden.includes(id))
}

/** Déplace un bloc d'un rang vers le haut (−1) ou le bas (+1) ; sans effet en bout de liste. */
export function moveBlock(order: readonly HomeBlockId[], id: HomeBlockId, delta: -1 | 1): HomeBlockId[] {
  const result = [...order]
  const from = result.indexOf(id)
  const to = from + delta
  if (from < 0 || to < 0 || to >= result.length) return result
  ;[result[from], result[to]] = [result[to]!, result[from]!]
  return result
}

/** Masque ou affiche un bloc ; la liste reste dans l'ordre d'affichage. */
export function toggleHidden(layout: HomeLayout, id: HomeBlockId): HomeLayout {
  const masked = new Set(layout.hidden)
  if (masked.has(id)) masked.delete(id)
  else masked.add(id)
  return { order: [...layout.order], hidden: layout.order.filter((b) => masked.has(b)) }
}
