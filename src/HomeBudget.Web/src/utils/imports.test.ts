import { describe, expect, it } from 'vitest'
import { defaultSelection, needsProfile, selectionSummary, toBase64, type ImportLine } from './imports'

const line = (n: number, status: ImportLine['status'], amount = -10): ImportLine => ({
  line: n,
  date: '2026-11-24',
  amount,
  label: `Ligne ${n}`,
  status,
  duplicateOfId: null,
  duplicateOfLabel: null,
  duplicateOfDate: null,
  suggestedPayeeName: null,
  suggestedCategoryId: null,
  occurrence: null,
})

describe('defaultSelection', () => {
  it('coche les opérations nouvelles et les échéances, pas les doublons', () => {
    const lines = [line(1, 'New'), line(2, 'Duplicate'), line(3, 'ProbableDuplicate'), line(4, 'Match')]
    expect(defaultSelection(lines)).toEqual([1, 4])
  })
})

describe('selectionSummary', () => {
  it('ignore les doublons certains et totalise au centime', () => {
    const lines = [line(1, 'New', -0.1), line(2, 'Match', -0.2), line(3, 'Duplicate', -5)]
    expect(selectionSummary(lines, [1, 2, 3], new Set([2]))).toEqual({ count: 2, matched: 1, total: -0.3 })
    expect(selectionSummary(lines, [2], new Set())).toEqual({ count: 1, matched: 0, total: -0.2 })
  })
})

describe('needsProfile', () => {
  it('demande un profil pour un CSV ou un TSV seulement', () => {
    expect(needsProfile('releve.csv')).toBe(true)
    expect(needsProfile('releve.TSV')).toBe(true)
    expect(needsProfile('releve.ofx')).toBe(false)
    expect(needsProfile('releve.QIF')).toBe(false)
  })
})

describe('toBase64', () => {
  it('encode des octets non UTF-8 (Windows-1252)', () => {
    expect(toBase64(new Uint8Array([0x4c, 0x69, 0x62, 0x65, 0x6c, 0x6c, 0xe9]))).toBe('TGliZWxs6Q==')
  })

  it('encode un gros fichier sans dépasser la pile', () => {
    const big = new Uint8Array(300_000).fill(65)
    expect(toBase64(big)).toHaveLength(400_000)
  })
})
