/** Erreur d'appel à l'API, avec un message prêt à afficher. */
export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

interface ProblemLike {
  title?: string | null
  detail?: string | null
  errors?: Record<string, string[]>
}

/** Extrait un message lisible d'une réponse « problem+json » (RFC 9457). */
export function problemMessage(problem: unknown, fallback = 'Une erreur est survenue.'): string {
  if (!problem || typeof problem !== 'object') return fallback
  const p = problem as ProblemLike
  if (p.errors && Object.keys(p.errors).length > 0) {
    return Object.values(p.errors).flat().join(' ')
  }
  return [p.title, p.detail].filter(Boolean).join(' ') || fallback
}

/** Renvoie les données d'une réponse openapi-fetch, ou lève une ApiError. */
export async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await call
  if (!response.ok) {
    throw new ApiError(problemMessage(error, messageForStatus(response.status)), response.status)
  }
  return data as T
}

function messageForStatus(status: number): string {
  switch (status) {
    case 401:
      return 'Session expirée : reconnectez-vous.'
    case 403:
      return 'Action non autorisée.'
    case 429:
      return 'Trop de tentatives : patientez une minute.'
    default:
      return status >= 500 ? 'Erreur du serveur : réessayez plus tard.' : 'Requête invalide.'
  }
}
