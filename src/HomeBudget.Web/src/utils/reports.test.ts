import { describe, expect, it } from 'vitest'
import { chartColors } from './charts'
import {
  emptyReportForm,
  formFromSaved,
  reportExportUrl,
  reportQuery,
  sankeyOption,
  savedReportBody,
  shiftPeriodKey,
  type Flows,
  type SavedReport,
} from './reports'

describe('reportQuery', () => {
  it('omet les listes vides et les dates hors plage saisie', () => {
    const form = { ...emptyReportForm(), from: '2026-01-01', to: '2026-01-31' }
    expect(reportQuery(form)).toEqual({
      range: 'CurrentPeriod',
      groupBy: 'Category',
      from: undefined,
      to: undefined,
      accountIds: undefined,
      categoryIds: undefined,
      payeeIds: undefined,
      tagIds: undefined,
      kinds: undefined,
      includeTransfers: undefined,
    })
    const custom = reportQuery({ ...form, range: 'Custom', kinds: ['Variable'], includeTransfers: true })
    expect(custom.from).toBe('2026-01-01')
    expect(custom.kinds).toEqual(['Variable'])
    expect(custom.includeTransfers).toBe(true)
  })

  it('répète les listes dans l’URL d’export', () => {
    const url = reportExportUrl({ ...emptyReportForm(), groupBy: 'Payee', accountIds: ['a', 'b'] }, 'xlsx')
    expect(url).toBe(
      '/api/reports/custom/export?format=xlsx&range=CurrentPeriod&groupBy=Payee&accountIds=a&accountIds=b',
    )
  })
})

describe('rapports enregistrés', () => {
  it('fait l’aller-retour entre le formulaire et l’enregistrement', () => {
    const saved = {
      id: '1',
      name: 'Repas',
      groupBy: 'Period',
      range: 'Custom',
      from: '2026-01-01',
      to: '2026-03-31',
      accountIds: [],
      categoryIds: ['c'],
      payeeIds: [],
      tagIds: ['t'],
      kinds: ['Variable'],
      includeTransfers: false,
      version: 1,
    } as SavedReport
    const form = formFromSaved(saved)
    expect(form.categoryIds).toEqual(['c'])
    expect(savedReportBody('  Repas ', form)).toMatchObject({
      name: 'Repas',
      from: '2026-01-01',
      tagIds: ['t'],
    })
    expect(savedReportBody('X', { ...form, range: 'CurrentYear' }).from).toBeNull()
  })
})

describe('sankeyOption', () => {
  const flows = {
    nodes: [
      { id: 'cat:1', label: 'Salaire', color: null, column: 0 },
      { id: 'income', label: 'Revenus', color: null, column: 1 },
      { id: 'kind:Variable', label: 'Dépenses variables', color: null, column: 2 },
      { id: 'cat:2', label: 'Divers', color: '#123456', column: 3 },
      { id: 'cat:3', label: 'Divers', color: null, column: 0 },
    ],
    links: [
      { source: 'cat:1', target: 'income', value: 100 },
      { source: 'income', target: 'kind:Variable', value: 40 },
      { source: 'kind:Variable', target: 'cat:2', value: 40 },
    ],
  } as unknown as Flows

  it('identifie les nœuds par leur id et affiche leur libellé', () => {
    const option = sankeyOption(flows, chartColors(false))
    const series = (
      option.series as {
        data: { name: string; depth: number; itemStyle: { color: string } }[]
        links: unknown[]
        label: { formatter: (p: unknown) => string }
      }[]
    )[0]!
    expect(series.data.map((d) => d.name)).toEqual(['cat:1', 'income', 'kind:Variable', 'cat:2', 'cat:3'])
    expect(series.data[3]!.itemStyle.color).toBe('#123456')
    expect(series.data[2]!.depth).toBe(2)
    expect(series.links).toHaveLength(3)
    expect(series.label.formatter({ name: 'cat:2' })).toBe('Divers')
    const tooltip = (option.tooltip as { formatter: (p: unknown) => string }).formatter
    expect(
      tooltip({ dataType: 'edge', data: { source: 'income', target: 'kind:Variable', value: 40 } }),
    ).toMatch(/^Revenus → Dépenses variables : 40,00\s€$/)
  })
})

describe('shiftPeriodKey', () => {
  it('recule et avance de mois en mois, en changeant d’année', () => {
    expect(shiftPeriodKey('2026-03', -2)).toBe('2026-01')
    expect(shiftPeriodKey('2026-03', -11)).toBe('2025-04')
    expect(shiftPeriodKey('2026-12', 1)).toBe('2027-01')
  })
})
