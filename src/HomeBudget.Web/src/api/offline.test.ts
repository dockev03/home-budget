import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from './errors'
import { isNetworkError, offlineGuard } from './offline'

const request = (method: string, path: string) => new Request(`https://localhost${path}`, { method })

describe('offlineGuard', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('laisse passer toutes les requêtes en ligne', async () => {
    vi.stubGlobal('navigator', { onLine: true })
    await expect(
      offlineGuard.onRequest({ request: request('POST', '/api/budgets/x') }),
    ).resolves.toBeUndefined()
  })

  it('hors ligne, refuse les modifications mais pas les lectures ni la déconnexion', async () => {
    vi.stubGlobal('navigator', { onLine: false })
    await expect(
      offlineGuard.onRequest({ request: request('GET', '/api/accounts') }),
    ).resolves.toBeUndefined()
    await expect(
      offlineGuard.onRequest({ request: request('POST', '/api/auth/logout') }),
    ).resolves.toBeUndefined()
    await expect(
      offlineGuard.onRequest({ request: request('PUT', '/api/accounts/1') }),
    ).rejects.toBeInstanceOf(ApiError)
  })
})

describe('isNetworkError', () => {
  it('reconnaît un fetch rejeté et le refus hors ligne', () => {
    expect(isNetworkError(new TypeError('Failed to fetch'))).toBe(true)
    expect(isNetworkError(new ApiError('Hors ligne', 0))).toBe(true)
    expect(isNetworkError(new ApiError('Refusé', 400))).toBe(false)
  })
})
