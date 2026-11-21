import createClient from 'openapi-fetch'
import { createCsrfMiddleware } from './csrf'
import type { components, paths } from './schema'

export type Schemas = components['schemas']
export type CurrentUser = Schemas['CurrentUser']

async function fetchCsrfToken(): Promise<string> {
  const response = await fetch('/api/auth/csrf', { credentials: 'same-origin' })
  if (!response.ok) throw new Error('Impossible d’obtenir le jeton anti-CSRF.')
  const body = (await response.json()) as Schemas['CsrfToken']
  return body.token
}

export const csrf = createCsrfMiddleware(fetchCsrfToken)

/** Client typé de l'API, généré depuis openapi.json (« npm run gen:api »). */
export const api = createClient<paths>({ baseUrl: '', credentials: 'same-origin' })
api.use(csrf)
