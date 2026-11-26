import { describe, expect, it } from 'vitest'
import {
  axisTooltip,
  balanceCurveOption,
  budgetBarsOption,
  chartColors,
  expensesDonutOption,
  historyOption,
  netWorthOption,
  shortPeriodLabel,
} from './charts'
import { formatAmount } from './format'

const colors = chartColors(false)

type Series = { name: string; data: unknown[]; lineStyle?: { type: string } }
const seriesOf = (option: { series?: unknown }) => option.series as Series[]

describe('chartColors', () => {
  it('change de palette avec le thème', () => {
    expect(chartColors(true).text).not.toBe(chartColors(false).text)
  })
})

describe('shortPeriodLabel', () => {
  it('abrège la période', () => {
    expect(shortPeriodLabel('2026-11')).toBe('nov. 26')
  })
})

describe('balanceCurveOption', () => {
  const daily = [
    { date: '2026-03-01', balance: 100, isForecast: false },
    { date: '2026-03-02', balance: 80, isForecast: false },
    { date: '2026-03-03', balance: 50, isForecast: true },
    { date: '2026-03-04', balance: -10, isForecast: true },
  ]

  it('trace le réel en plein, le prévu en pointillés depuis le dernier jour réel, et le seuil', () => {
    const [real, forecast, threshold] = seriesOf(balanceCurveOption(daily, 0, colors))
    expect(real!.data).toEqual([100, 80, null, null])
    expect(real!.lineStyle!.type).toBe('solid')
    expect(forecast!.data).toEqual([null, 80, 50, -10])
    expect(forecast!.lineStyle!.type).toBe('dashed')
    expect(threshold!.name).toBe("Seuil d'alerte")
    expect(threshold!.data).toEqual([0, 0, 0, 0])
  })

  it('ajoute la série des scénarios à partir de ses dates, sans toucher au prévu', () => {
    const scenario = [
      { date: '2026-03-02', balance: 80, isForecast: false },
      { date: '2026-03-03', balance: 30, isForecast: true },
      { date: '2026-03-04', balance: -30, isForecast: true },
    ]
    const series = seriesOf(balanceCurveOption(daily, 0, colors, scenario))
    expect(series).toHaveLength(4)
    expect(series[1]!.data).toEqual([null, 80, 50, -10])
    expect(series[3]!.name).toBe('Avec scénarios')
    expect(series[3]!.data).toEqual([null, 80, 30, -30])
    expect(seriesOf(balanceCurveOption(daily, 0, colors))).toHaveLength(3)
  })

  it('montre toute la période en prévu quand aucun jour n’est passé', () => {
    const future = daily.map((p) => ({ ...p, isForecast: true }))
    const [real, forecast] = seriesOf(balanceCurveOption(future, 0, colors))
    expect(real!.data).toEqual([null, null, null, null])
    expect(forecast!.data).toEqual([100, 80, 50, -10])
  })
})

describe('expensesDonutOption', () => {
  it('reprend les montants et les couleurs des catégories', () => {
    const [pie] = seriesOf(
      expensesDonutOption(
        [
          { categoryId: 'a', name: 'Courses', color: '#e65100', kind: 'Variable', amount: 120 },
          { categoryId: 'b', name: 'Divers', color: null, kind: 'Variable', amount: 30 },
        ],
        colors,
      ),
    )
    expect(pie!.data).toEqual([
      { name: 'Courses', value: 120, itemStyle: { color: '#e65100' } },
      { name: 'Divers', value: 30, itemStyle: undefined },
    ])
  })
})

describe('budgetBarsOption', () => {
  it('colore le réel selon la jauge', () => {
    const [budget, actual] = seriesOf(
      budgetBarsOption(
        [
          {
            categoryId: 'a',
            name: 'Courses',
            color: null,
            kind: 'Variable',
            budgeted: 400,
            actual: 450,
            status: 'Exceeded',
          },
        ],
        colors,
      ),
    )
    expect(budget!.data).toEqual([400])
    expect(actual!.data).toEqual([{ value: 450, itemStyle: { color: colors.status.Exceeded } }])
  })
})

describe('historyOption', () => {
  it('donne revenus, dépenses et épargne par période', () => {
    const option = historyOption(
      [
        {
          key: '2026-10',
          start: '2026-10-01',
          end: '2026-10-31',
          income: 2000,
          expenses: 1500,
          savings: 200,
        },
        {
          key: '2026-11',
          start: '2026-11-01',
          end: '2026-11-30',
          income: 2100,
          expenses: 1400,
          savings: 300,
        },
      ],
      colors,
    )
    expect((option.xAxis as { data: string[] }).data).toEqual(['oct. 26', 'nov. 26'])
    expect(seriesOf(option).map((s) => [s.name, s.data])).toEqual([
      ['Revenus', [2000, 2100]],
      ['Dépenses', [1500, 1400]],
      ['Épargne', [200, 300]],
    ])
  })
})

describe('axisTooltip', () => {
  it('affiche les montants au format français et ignore les valeurs absentes', () => {
    const text = axisTooltip([
      { name: '02/03', seriesName: 'Réel', value: 1234.5, marker: '' },
      { name: '02/03', seriesName: 'Prévu', value: null, marker: '' },
    ])
    expect(text).toBe(`02/03<br>Réel : ${formatAmount(1234.5)}`)
  })
})

describe('netWorthOption', () => {
  it('trace le total puis une courbe par type de compte présent', () => {
    const option = netWorthOption(
      [
        {
          key: '2026-01',
          date: '2026-01-31',
          total: 8500,
          byType: [
            { type: 'Checking', amount: 1000 },
            { type: 'Investment', amount: 7500 },
          ],
        },
        { key: '2026-02', date: '2026-02-10', total: 8300, byType: [{ type: 'Investment', amount: 8300 }] },
      ],
      colors,
    )
    const series = seriesOf(option)
    expect(series.map((s) => s.name)).toEqual(['Patrimoine net', 'Compte courant', 'Investissement'])
    expect(series[0]!.data).toEqual([8500, 8300])
    expect(series[1]!.data).toEqual([1000, 0])
    expect(series[1]!.lineStyle?.type).toBe('dashed')
  })
})
