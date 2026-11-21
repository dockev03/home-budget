import { describe, expect, it } from 'vitest'
import { ApiError, problemMessage, unwrap } from './errors'

describe('problemMessage', () => {
  it('assemble les erreurs de validation', () => {
    expect(problemMessage({ errors: { a: ['Trop court.'], b: ['Déjà utilisé.'] } })).toBe(
      'Trop court. Déjà utilisé.',
    )
  })

  it('utilise le titre et le détail', () => {
    expect(problemMessage({ title: 'Compte verrouillé.', detail: 'Réessayez plus tard.' })).toBe(
      'Compte verrouillé. Réessayez plus tard.',
    )
  })

  it('renvoie le message par défaut', () => {
    expect(problemMessage(undefined, 'Défaut')).toBe('Défaut')
  })
})

describe('unwrap', () => {
  it('lève une ApiError avec le statut', async () => {
    const call = Promise.resolve({ error: undefined, response: new Response(null, { status: 429 }) })
    await expect(unwrap(call)).rejects.toEqual(
      new ApiError('Trop de tentatives : patientez une minute.', 429),
    )
  })

  it('renvoie les données si la réponse est OK', async () => {
    const call = Promise.resolve({ data: { ok: true }, response: new Response(null, { status: 200 }) })
    await expect(unwrap(call)).resolves.toEqual({ ok: true })
  })
})
