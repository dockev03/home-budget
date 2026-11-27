import { describe, expect, it } from 'vitest'
import { avatarUrl, squareCrop } from './avatar'

describe('avatarUrl', () => {
  it('versionne l’adresse avec la date de la photo', () => {
    expect(avatarUrl('2026-11-27T16:30:00+00:00')).toBe(
      '/api/account/avatar?v=2026-11-27T16%3A30%3A00%2B00%3A00',
    )
  })

  it('ne renvoie rien sans photo', () => {
    expect(avatarUrl(null)).toBeNull()
    expect(avatarUrl(undefined)).toBeNull()
  })
})

describe('squareCrop', () => {
  it('centre le carré dans une photo en paysage', () => {
    expect(squareCrop(4000, 3000)).toEqual({ x: 500, y: 0, size: 3000 })
  })

  it('centre le carré dans une photo en portrait', () => {
    expect(squareCrop(3000, 4001)).toEqual({ x: 0, y: 500, size: 3000 })
  })

  it('garde une image déjà carrée', () => {
    expect(squareCrop(256, 256)).toEqual({ x: 0, y: 0, size: 256 })
  })
})
