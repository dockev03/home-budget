import { describe, expect, it } from 'vitest'
import {
  formatAmount,
  formatDate,
  formatDayHeading,
  formatSignedAmount,
  parseAmount,
  roundCents,
  toIso,
} from './format'

/** Intl utilise des espaces insécables (fines) : on les normalise pour comparer. */
const plain = (text: string) => text.replace(/[  ]/g, ' ')

describe('formatAmount', () => {
  it('formate à la française', () => {
    expect(plain(formatAmount(1234.56))).toBe('1 234,56 €')
    expect(plain(formatAmount(-12.5))).toBe('-12,50 €')
  })

  it('signe les entrées', () => {
    expect(plain(formatSignedAmount(10))).toBe('+10,00 €')
    expect(plain(formatSignedAmount(-10))).toBe('-10,00 €')
  })
})

describe('dates', () => {
  it('affiche JJ/MM/AAAA', () => {
    expect(formatDate('2026-03-07')).toBe('07/03/2026')
  })

  it('affiche le jour en toutes lettres', () => {
    expect(formatDayHeading('2026-11-21')).toBe('Samedi 21 novembre 2026')
  })

  it('convertit en ISO sans fuseau horaire', () => {
    expect(toIso(new Date(2026, 0, 5))).toBe('2026-01-05')
  })
})

describe('parseAmount', () => {
  it.each([
    ['12', 12],
    ['12,5', 12.5],
    ['-12.50', -12.5],
    ['1 234,56', 1234.56],
    ['1 234,56 €', 1234.56],
    ['+3', 3],
  ])('lit « %s »', (text, expected) => {
    expect(parseAmount(text)).toBe(expected)
  })

  it.each(['', 'abc', '12,345', '1,2,3', '--1'])('refuse « %s »', (text) => {
    expect(parseAmount(text)).toBeNull()
  })
})

describe('roundCents', () => {
  it('corrige les erreurs de virgule flottante', () => {
    expect(roundCents(0.1 + 0.2)).toBe(0.3)
  })
})
