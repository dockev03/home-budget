import { describe, expect, it, vi } from 'vitest'
import { CSRF_HEADER, createCsrfMiddleware } from './csrf'

const request = (method: string) => new Request('https://localhost/api/test', { method })
const call = (mw: ReturnType<typeof createCsrfMiddleware>, req: Request) => mw.onRequest({ request: req })

describe('createCsrfMiddleware', () => {
  it('ne touche pas aux requêtes GET', async () => {
    const fetchToken = vi.fn(async () => 'token')
    const result = await call(createCsrfMiddleware(fetchToken), request('GET'))
    expect(result).toBeUndefined()
    expect(fetchToken).not.toHaveBeenCalled()
  })

  it('ajoute le jeton aux POST et ne le demande qu’une fois', async () => {
    const fetchToken = vi.fn(async () => 'abc')
    const mw = createCsrfMiddleware(fetchToken)
    const first = (await call(mw, request('POST'))) as Request
    const second = (await call(mw, request('DELETE'))) as Request
    expect(first.headers.get(CSRF_HEADER)).toBe('abc')
    expect(second.headers.get(CSRF_HEADER)).toBe('abc')
    expect(fetchToken).toHaveBeenCalledTimes(1)
  })

  it('redemande un jeton après reset()', async () => {
    const fetchToken = vi.fn().mockResolvedValueOnce('one').mockResolvedValueOnce('two')
    const mw = createCsrfMiddleware(fetchToken)
    await call(mw, request('POST'))
    mw.reset()
    const req = (await call(mw, request('POST'))) as Request
    expect(req.headers.get(CSRF_HEADER)).toBe('two')
  })

  it('oublie le jeton quand le serveur le refuse', async () => {
    const fetchToken = vi.fn().mockResolvedValueOnce('stale').mockResolvedValueOnce('fresh')
    const mw = createCsrfMiddleware(fetchToken)
    const req = (await call(mw, request('POST'))) as Request
    const response = new Response(JSON.stringify({ title: 'Jeton anti-CSRF absent ou invalide.' }), {
      status: 400,
    })
    await mw.onResponse({ request: req, response })
    const retried = (await call(mw, request('POST'))) as Request
    expect(retried.headers.get(CSRF_HEADER)).toBe('fresh')
  })
})
