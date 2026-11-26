/** Cache des réponses GET de l'API, rempli par le service worker (Workbox, stratégie « réseau d'abord »). */
export const API_CACHE = 'hb-api'

/** Vide le cache de lecture : à la déconnexion, les données du foyer ne restent pas sur l'appareil. */
export async function clearApiCache(): Promise<void> {
  if (typeof caches === 'undefined') return
  await caches.delete(API_CACHE)
}

/** « 2026-11 » → « 2026-10 ». */
export function previousPeriodKey(key: string): string {
  const [year, month] = key.split('-').map(Number) as [number, number]
  return month === 1 ? `${year - 1}-12` : `${year}-${String(month - 1).padStart(2, '0')}`
}

/**
 * Données consultables hors ligne (L4) : la période en cours et les deux précédentes (tableau de bord, budget,
 * calendrier), et la prévision. Les données de référence (comptes, catégories, tiers) sont chargées par l'appli
 * au démarrage. Le service worker garde chaque réponse ; un échec est sans conséquence.
 */
export async function warmUpCache(currentPeriod: string): Promise<void> {
  if (typeof navigator === 'undefined' || !navigator.serviceWorker?.controller) return
  const periods = [currentPeriod]
  while (periods.length < 3) periods.push(previousPeriodKey(periods[periods.length - 1]!))
  const urls = [
    '/api/forecast',
    '/api/payees',
    '/api/tags',
    '/api/recurrences/pending',
    ...periods.flatMap((p) => [`/api/dashboard/${p}`, `/api/budgets/${p}`, `/api/calendar/${p}`]),
  ]
  await Promise.allSettled(urls.map((url) => fetch(url, { credentials: 'same-origin' })))
}
