import { ApiError } from './errors'

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

/** Chemins utilisables hors ligne : la connexion et la déconnexion restent possibles (échec réseau explicite). */
const ALLOWED_OFFLINE = ['/api/auth/']

export const OFFLINE_MESSAGE =
  'Hors ligne : seule la saisie d’opérations est possible ; elle sera envoyée au retour du réseau.'

/**
 * Hors ligne, l'appli est en lecture seule (L4) : une modification est refusée tout de suite, au lieu d'échouer
 * après le délai réseau. La saisie d'une opération passe par la file d'attente, pas par ce client.
 */
export const offlineGuard = {
  async onRequest({ request }: { request: Request }) {
    if (typeof navigator === 'undefined' || navigator.onLine) return undefined
    if (!UNSAFE_METHODS.has(request.method.toUpperCase())) return undefined
    const path = new URL(request.url, 'https://localhost').pathname
    if (ALLOWED_OFFLINE.some((p) => path.startsWith(p))) return undefined
    throw new ApiError(OFFLINE_MESSAGE, 0)
  },
}

/** Erreur due au réseau (fetch rejeté), à distinguer d'une réponse d'erreur du serveur. */
export function isNetworkError(error: unknown): boolean {
  return error instanceof TypeError || (error instanceof ApiError && error.status === 0)
}
