import { describe, expect, it } from 'vitest'
import { categoryRows, formatPercent, kindRows, type Comparison } from './comparison'

const period = (key: string, income: number, fixed: number, variable: number, savings: number) => ({
  key,
  start: `${key}-01`,
  end: `${key}-28`,
  income,
  fixed,
  variable,
  savings,
})

const comparison: Comparison = {
  period: {
    key: '2026-03',
    start: '2026-03-01',
    end: '2026-03-31',
    previousKey: '2026-02',
    nextKey: '2026-04',
    isCurrent: true,
  },
  current: period('2026-03', 2000, 600, 150, 200),
  previous: period('2026-02', 1800, 600, 300, 0),
  lastYear: period('2025-03', 0, 500, 100, 100),
  categories: [
    {
      categoryId: 'c',
      name: 'Courses',
      color: null,
      kind: 'Variable',
      current: 100,
      previous: 300,
      lastYear: 100,
    },
    {
      categoryId: 's',
      name: 'Salaire',
      color: null,
      kind: 'Income',
      current: 2000,
      previous: 1800,
      lastYear: 0,
    },
    { categoryId: 'z', name: 'Rien', color: null, kind: 'Variable', current: 0, previous: 0, lastYear: 5 },
  ],
}

describe('kindRows', () => {
  it('calcule les écarts et leur sens, par nature', () => {
    const [income, fixed, variable, savings] = kindRows(comparison, 'previous')
    expect(income).toMatchObject({ label: 'Revenus', delta: 200, percent: 11.1, favorable: true })
    expect(fixed).toMatchObject({ delta: 0, percent: 0, favorable: null })
    expect(variable).toMatchObject({ delta: -150, percent: -50, favorable: true })
    expect(savings).toMatchObject({ delta: 200, percent: null, favorable: true })
  })

  it('compare à la même période un an avant', () => {
    const fixed = kindRows(comparison, 'lastYear')[1]!
    expect(fixed).toMatchObject({ reference: 500, delta: 100, percent: 20, favorable: false })
  })
})

describe('categoryRows', () => {
  it('classe par écart décroissant et écarte les lignes vides', () => {
    const rows = categoryRows(comparison, 'previous')
    expect(rows.map((r) => r.key)).toEqual(['c', 's'])
    expect(categoryRows(comparison, 'lastYear').map((r) => r.key)).toEqual(['s', 'z', 'c'])
  })
})

describe('formatPercent', () => {
  it('affiche le signe et la virgule', () => {
    expect(formatPercent(12.5)).toBe('+12,5 %')
    expect(formatPercent(-3)).toBe('−3 %')
    expect(formatPercent(0)).toBe('0 %')
    expect(formatPercent(null)).toBe('—')
  })
})
