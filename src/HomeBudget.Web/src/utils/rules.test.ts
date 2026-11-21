import { describe, expect, it } from 'vitest'
import { minLength, required, totpCode } from './rules'

describe('règles de validation', () => {
  it('required', () => {
    expect(required('')).toBe('Champ obligatoire.')
    expect(required('  ')).toBe('Champ obligatoire.')
    expect(required('ok')).toBe(true)
  })

  it('minLength', () => {
    expect(minLength(12)('court')).toBe('12 caractères minimum.')
    expect(minLength(12)('une phrase longue')).toBe(true)
  })

  it('totpCode', () => {
    expect(totpCode('123 456')).toBe(true)
    expect(totpCode('12345')).toBe('Le code comporte 6 chiffres.')
  })
})
