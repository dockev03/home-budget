export const CSRF_HEADER = 'X-CSRF-TOKEN'
const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

/** Middleware openapi-fetch (compatible avec son type « Middleware »). */
export interface CsrfMiddleware {
  onRequest(params: { request: Request }): Promise<Request | undefined>
  onResponse(params: { request: Request; response: Response }): Promise<undefined>
  /** Oublie le jeton : à appeler quand l'utilisateur change (connexion, déconnexion). */
  reset(): void
}

/**
 * Ajoute le jeton anti-CSRF aux requêtes qui modifient l'état. Le jeton est demandé une seule fois
 * puis réutilisé ; il est oublié si le serveur le refuse, pour être redemandé à la requête suivante.
 */
export function createCsrfMiddleware(fetchToken: () => Promise<string>): CsrfMiddleware {
  let token: Promise<string> | null = null

  return {
    async onRequest({ request }) {
      if (!UNSAFE_METHODS.has(request.method.toUpperCase())) return undefined
      token ??= fetchToken().catch((error: unknown) => {
        token = null
        throw error
      })
      request.headers.set(CSRF_HEADER, await token)
      return request
    },
    async onResponse({ request, response }) {
      if (response.status === 400 && UNSAFE_METHODS.has(request.method.toUpperCase())) {
        const body = await response
          .clone()
          .json()
          .catch(() => null)
        if (typeof body?.title === 'string' && body.title.includes('anti-CSRF')) token = null
      }
      return undefined
    },
    reset() {
      token = null
    },
  }
}
