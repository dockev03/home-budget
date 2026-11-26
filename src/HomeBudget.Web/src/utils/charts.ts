import type { EChartsOption } from 'echarts'
import type { Schemas } from '@/api/client'
import { formatAmount, formatDate } from './format'
import { accountTypeTitle } from './labels'

/** Options ECharts du tableau de bord : fonctions pures, sans dépendance à l'affichage. */

export interface ChartColors {
  text: string
  income: string
  expenses: string
  savings: string
  budget: string
  real: string
  forecast: string
  scenario: string
  threshold: string
  status: Record<Schemas['BudgetStatus'], string>
}

/** Couleurs lisibles sur fond clair ou sombre (thème suivi, G10). */
export function chartColors(dark: boolean): ChartColors {
  return dark
    ? {
        text: '#e0e0e0',
        income: '#66bb6a',
        expenses: '#ff8a65',
        savings: '#7ab0ff',
        budget: '#78909c',
        real: '#7ab0ff',
        forecast: '#b3a8ff',
        scenario: '#ffb74d',
        threshold: '#ef5350',
        status: { None: '#7ab0ff', Ok: '#66bb6a', Warning: '#ffb74d', Exceeded: '#ef5350' },
      }
    : {
        text: '#424242',
        income: '#2e7d32',
        expenses: '#e65100',
        savings: '#1e5aa8',
        budget: '#b0bec5',
        real: '#1e5aa8',
        forecast: '#4a3aa5',
        scenario: '#b45309',
        threshold: '#c62828',
        status: { None: '#1e5aa8', Ok: '#2e7d32', Warning: '#ef6c00', Exceeded: '#c62828' },
      }
}

const compact = new Intl.NumberFormat('fr-FR', {
  style: 'currency',
  currency: 'EUR',
  notation: 'compact',
  maximumFractionDigits: 1,
})
const shortMonth = new Intl.DateTimeFormat('fr-FR', { month: 'short', year: '2-digit' })

/** « 1,2 k€ » : graduations des axes. */
export function compactAmount(value: number): string {
  return compact.format(value)
}

/** Clé de période « 2026-11 » → « nov. 26 ». */
export function shortPeriodLabel(key: string): string {
  const [year, month] = key.split('-').map(Number)
  return shortMonth.format(new Date(year!, month! - 1, 1))
}

interface TooltipParam {
  marker?: string
  seriesName?: string
  name?: string
  value?: unknown
}

/** Infobulle d'axe : une ligne par série, montants au format français. */
export function axisTooltip(params: unknown): string {
  const list = (Array.isArray(params) ? params : [params]) as TooltipParam[]
  const lines = list
    .filter((p) => typeof p.value === 'number')
    .map((p) => `${p.marker ?? ''}${p.seriesName ?? ''} : ${formatAmount(p.value as number)}`)
  return [list[0]?.name ?? '', ...lines].join('<br>')
}

function base(colors: ChartColors): EChartsOption {
  return {
    backgroundColor: 'transparent',
    textStyle: { color: colors.text, fontFamily: 'inherit' },
    animationDuration: 300,
  }
}

const valueAxis = (colors: ChartColors) => ({
  type: 'value' as const,
  axisLabel: { color: colors.text, formatter: (v: number) => compactAmount(v) },
  splitLine: { lineStyle: { opacity: 0.3 } },
})

/** G2 : anneau des dépenses par catégorie, aux couleurs des catégories. */
export function expensesDonutOption(
  items: Schemas['CategoryAmountDto'][],
  colors: ChartColors,
): EChartsOption {
  return {
    ...base(colors),
    tooltip: {
      trigger: 'item',
      formatter: (p: unknown) => {
        const { name, value, percent } = p as { name: string; value: number; percent: number }
        return `${name} : ${formatAmount(value)} (${percent.toLocaleString('fr-FR')} %)`
      },
    },
    legend: { type: 'scroll', bottom: 0, textStyle: { color: colors.text } },
    series: [
      {
        name: 'Dépenses',
        type: 'pie',
        radius: ['45%', '70%'],
        center: ['50%', '45%'],
        avoidLabelOverlap: true,
        label: { show: false },
        data: items.map((c) => ({
          name: c.name,
          value: c.amount,
          itemStyle: c.color ? { color: c.color } : undefined,
        })),
      },
    ],
  }
}

/** G3 : budget comparé au réel, une paire de barres horizontales par catégorie ; le réel prend la couleur de sa jauge. */
export function budgetBarsOption(
  items: Schemas['BudgetComparisonDto'][],
  colors: ChartColors,
): EChartsOption {
  return {
    ...base(colors),
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' }, formatter: axisTooltip },
    legend: { bottom: 0, textStyle: { color: colors.text } },
    grid: { left: 8, right: 16, top: 8, bottom: 32, containLabel: true },
    xAxis: valueAxis(colors),
    yAxis: {
      type: 'category',
      inverse: true,
      data: items.map((i) => i.name),
      axisLabel: { color: colors.text, width: 110, overflow: 'truncate' },
    },
    series: [
      {
        name: 'Budget',
        type: 'bar',
        itemStyle: { color: colors.budget },
        data: items.map((i) => i.budgeted),
      },
      {
        name: 'Réel',
        type: 'bar',
        itemStyle: { color: colors.real },
        data: items.map((i) => ({ value: i.actual, itemStyle: { color: colors.status[i.status] } })),
      },
    ],
  }
}

/** G4 : revenus, dépenses et épargne des 12 dernières périodes. */
export function historyOption(history: Schemas['PeriodTotalsDto'][], colors: ChartColors): EChartsOption {
  const serie = (name: string, color: string, values: number[]) => ({
    name,
    type: 'bar' as const,
    itemStyle: { color },
    data: values,
  })
  return {
    ...base(colors),
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' }, formatter: axisTooltip },
    legend: { bottom: 0, textStyle: { color: colors.text } },
    grid: { left: 8, right: 8, top: 8, bottom: 32, containLabel: true },
    xAxis: {
      type: 'category',
      data: history.map((h) => shortPeriodLabel(h.key)),
      axisLabel: { color: colors.text },
    },
    yAxis: valueAxis(colors),
    series: [
      serie(
        'Revenus',
        colors.income,
        history.map((h) => h.income),
      ),
      serie(
        'Dépenses',
        colors.expenses,
        history.map((h) => h.expenses),
      ),
      serie(
        'Épargne',
        colors.savings,
        history.map((h) => h.savings),
      ),
    ],
  }
}

/**
 * F2 : courbe du solde jour par jour. Le réel est en trait plein, le prévu en pointillés à partir du dernier
 * jour réel (pour que les deux courbes se rejoignent), et le seuil d'alerte en ligne horizontale.
 * F5 : la série « Avec scénarios », d'aujourd'hui à la fin de l'horizon, s'ajoute à part.
 */
export function balanceCurveOption(
  daily: Schemas['ForecastPointDto'][],
  threshold: number,
  colors: ChartColors,
  scenario: Schemas['ForecastPointDto'][] | null = null,
): EChartsOption {
  const scenarioByDate = new Map((scenario ?? []).map((p) => [p.date, p.balance]))
  const lastReal = daily.reduce((last, p, i) => (p.isForecast ? last : i), -1)
  const line = (name: string, color: string, data: (number | null)[], dashed: boolean) => ({
    name,
    type: 'line' as const,
    data,
    showSymbol: false,
    connectNulls: false,
    itemStyle: { color },
    lineStyle: { color, width: 2, type: dashed ? ('dashed' as const) : ('solid' as const) },
  })
  return {
    ...base(colors),
    tooltip: { trigger: 'axis', formatter: axisTooltip },
    legend: { bottom: 0, textStyle: { color: colors.text } },
    grid: { left: 8, right: 16, top: 16, bottom: 32, containLabel: true },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: daily.map((p) => formatDate(p.date).slice(0, 5)),
      axisLabel: { color: colors.text },
    },
    yAxis: { ...valueAxis(colors), scale: true },
    series: [
      line(
        'Réel',
        colors.real,
        daily.map((p, i) => (i <= lastReal ? p.balance : null)),
        false,
      ),
      line(
        'Prévu',
        colors.forecast,
        daily.map((p, i) => (i >= Math.max(lastReal, 0) ? p.balance : null)),
        true,
      ),
      {
        ...line(
          "Seuil d'alerte",
          colors.threshold,
          daily.map(() => threshold),
          true,
        ),
        lineStyle: { color: colors.threshold, width: 1, type: 'dotted' as const },
      },
      ...(scenario
        ? [
            line(
              'Avec scénarios',
              colors.scenario,
              daily.map((p) => scenarioByDate.get(p.date) ?? null),
              true,
            ),
          ]
        : []),
    ],
  }
}

/** G7 : top des tiers de la période, en barres horizontales (le plus dépensier en haut). */
export function topPayeesOption(items: Schemas['PayeeAmountDto'][], colors: ChartColors): EChartsOption {
  return {
    ...base(colors),
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' }, formatter: axisTooltip },
    grid: { left: 8, right: 16, top: 8, bottom: 8, containLabel: true },
    xAxis: valueAxis(colors),
    yAxis: {
      type: 'category',
      inverse: true,
      data: items.map((i) => i.name),
      axisLabel: { color: colors.text, width: 110, overflow: 'truncate' },
    },
    series: [
      {
        name: 'Dépenses',
        type: 'bar',
        itemStyle: { color: colors.expenses },
        data: items.map((i) => i.amount),
      },
    ],
  }
}

/**
 * G5 : patrimoine net en fin de chaque période. Le total est une aire pleine ; chaque type de compte présent sur la
 * durée a sa propre courbe, masquable depuis la légende.
 */
export function netWorthOption(points: Schemas['NetWorthPointDto'][], colors: ChartColors): EChartsOption {
  const typeColors: Record<Schemas['AccountType'], string> = {
    Checking: colors.budget,
    Savings: colors.income,
    Investment: colors.forecast,
    Cash: colors.scenario,
    Credit: colors.threshold,
  }
  const types = (Object.keys(typeColors) as Schemas['AccountType'][]).filter((type) =>
    points.some((p) => p.byType.some((t) => t.type === type)),
  )
  const line = (name: string, color: string, data: number[], total: boolean) => ({
    name,
    type: 'line' as const,
    data,
    showSymbol: false,
    itemStyle: { color },
    lineStyle: { color, width: total ? 2 : 1, type: total ? ('solid' as const) : ('dashed' as const) },
    ...(total ? { areaStyle: { color, opacity: 0.15 } } : {}),
  })
  return {
    ...base(colors),
    tooltip: { trigger: 'axis', formatter: axisTooltip },
    legend: { type: 'scroll', bottom: 0, textStyle: { color: colors.text } },
    grid: { left: 8, right: 16, top: 16, bottom: 32, containLabel: true },
    xAxis: {
      type: 'category',
      boundaryGap: false,
      data: points.map((p) => shortPeriodLabel(p.key)),
      axisLabel: { color: colors.text },
    },
    yAxis: { ...valueAxis(colors), scale: true },
    series: [
      line(
        'Patrimoine net',
        colors.real,
        points.map((p) => p.total),
        true,
      ),
      ...types.map((type) =>
        line(
          accountTypeTitle(type),
          typeColors[type],
          points.map((p) => p.byType.find((t) => t.type === type)?.amount ?? 0),
          false,
        ),
      ),
    ],
  }
}
