import type { EChartsOption } from 'echarts'
import type { Schemas } from '@/api/client'
import type { ChartColors } from './charts'
import { formatAmount } from './format'

export type ReportGroupBy = Schemas['ReportGroupBy']
export type ReportRange = Schemas['ReportRange']
export type Report = Schemas['ReportDto']
export type SavedReport = Schemas['SavedReportDto']
export type Flows = Schemas['FlowsDto']
type CategoryKind = Schemas['CategoryKind']

export const groupByOptions: { value: ReportGroupBy; title: string }[] = [
  { value: 'Category', title: 'Catégorie' },
  { value: 'Subcategory', title: 'Sous-catégorie' },
  { value: 'Payee', title: 'Tiers' },
  { value: 'Tag', title: 'Tag' },
  { value: 'Period', title: 'Période' },
  { value: 'Account', title: 'Compte' },
]

export const rangeOptions: { value: ReportRange; title: string }[] = [
  { value: 'CurrentPeriod', title: 'Période en cours' },
  { value: 'PreviousPeriod', title: 'Période précédente' },
  { value: 'Last3Periods', title: '3 dernières périodes' },
  { value: 'Last12Periods', title: '12 dernières périodes' },
  { value: 'CurrentYear', title: 'Année en cours' },
  { value: 'PreviousYear', title: 'Année précédente' },
  { value: 'Custom', title: 'Dates choisies' },
]

/** Définition d'un rapport, telle que saisie (G8). */
export interface ReportForm {
  groupBy: ReportGroupBy
  range: ReportRange
  from: string
  to: string
  accountIds: string[]
  categoryIds: string[]
  payeeIds: string[]
  tagIds: string[]
  kinds: CategoryKind[]
  includeTransfers: boolean
}

export function emptyReportForm(): ReportForm {
  return {
    groupBy: 'Category',
    range: 'CurrentPeriod',
    from: '',
    to: '',
    accountIds: [],
    categoryIds: [],
    payeeIds: [],
    tagIds: [],
    kinds: [],
    includeTransfers: false,
  }
}

/** Formulaire → paramètres de /api/reports/custom (les listes vides et les dates inutiles sont omises). */
export function reportQuery(form: ReportForm) {
  const custom = form.range === 'Custom'
  return {
    range: form.range,
    groupBy: form.groupBy,
    from: custom && form.from ? form.from : undefined,
    to: custom && form.to ? form.to : undefined,
    accountIds: form.accountIds.length ? form.accountIds : undefined,
    categoryIds: form.categoryIds.length ? form.categoryIds : undefined,
    payeeIds: form.payeeIds.length ? form.payeeIds : undefined,
    tagIds: form.tagIds.length ? form.tagIds : undefined,
    kinds: form.kinds.length ? form.kinds : undefined,
    includeTransfers: form.includeTransfers || undefined,
  }
}

/** URL d'export du rapport : mêmes paramètres, listes répétées (?accountIds=a&accountIds=b). */
export function reportExportUrl(form: ReportForm, format: 'csv' | 'xlsx'): string {
  const query = new URLSearchParams({ format })
  for (const [key, value] of Object.entries(reportQuery(form))) {
    if (value === undefined) continue
    for (const item of Array.isArray(value) ? value : [value]) query.append(key, String(item))
  }
  return `/api/reports/custom/export?${query.toString()}`
}

/** Rapport enregistré → formulaire. */
export function formFromSaved(saved: SavedReport): ReportForm {
  return {
    groupBy: saved.groupBy,
    range: saved.range,
    from: saved.from ?? '',
    to: saved.to ?? '',
    accountIds: [...saved.accountIds],
    categoryIds: [...saved.categoryIds],
    payeeIds: [...saved.payeeIds],
    tagIds: [...saved.tagIds],
    kinds: [...saved.kinds],
    includeTransfers: saved.includeTransfers,
  }
}

/** Formulaire → corps d'enregistrement. */
export function savedReportBody(name: string, form: ReportForm) {
  const custom = form.range === 'Custom'
  return {
    name: name.trim(),
    groupBy: form.groupBy,
    range: form.range,
    from: custom ? form.from || null : null,
    to: custom ? form.to || null : null,
    accountIds: form.accountIds,
    categoryIds: form.categoryIds,
    payeeIds: form.payeeIds,
    tagIds: form.tagIds,
    kinds: form.kinds,
    includeTransfers: form.includeTransfers,
  }
}

/** Barres du rapport : sorties et entrées par groupe (15 premiers groupes, ou toutes les périodes). */
export function reportBarsOption(report: Report, colors: ChartColors, maxRows = 15): EChartsOption {
  const rows = report.groupBy === 'Period' ? report.rows : report.rows.slice(0, maxRows)
  return {
    textStyle: { color: colors.text },
    tooltip: {
      trigger: 'axis',
      valueFormatter: (v) => formatAmount(Number(v)),
    },
    legend: { bottom: 0, textStyle: { color: colors.text } },
    grid: { left: 8, right: 16, top: 16, bottom: 40, containLabel: true },
    xAxis: {
      type: 'category',
      data: rows.map((r) => r.label),
      axisLabel: { interval: 0, rotate: 30, color: colors.text },
    },
    yAxis: { type: 'value', axisLabel: { color: colors.text } },
    series: [
      { name: 'Sorties', type: 'bar', data: rows.map((r) => r.debit), itemStyle: { color: colors.expenses } },
      { name: 'Entrées', type: 'bar', data: rows.map((r) => r.credit), itemStyle: { color: colors.income } },
    ],
  }
}

/**
 * Diagramme de Sankey (G9) : les nœuds sont identifiés par leur id (deux catégories peuvent avoir le même nom),
 * leur libellé est affiché à part.
 */
export function sankeyOption(flows: Flows, colors: ChartColors): EChartsOption {
  const labels = new Map(flows.nodes.map((n) => [n.id, n.label]))
  const nodeColor = (id: string, color: string | null | undefined) => {
    if (color) return color
    if (id === 'income' || id.startsWith('kind:Savings')) return colors.savings
    if (id === 'unspent') return colors.income
    if (id === 'reserves') return colors.threshold
    return id.startsWith('kind:') ? colors.expenses : colors.budget
  }
  return {
    textStyle: { color: colors.text },
    tooltip: {
      trigger: 'item',
      formatter: (params: unknown) => {
        const p = params as {
          dataType?: string
          data: { source?: string; target?: string; name?: string; value?: number }
        }
        if (p.dataType === 'edge')
          return `${labels.get(p.data.source!)} → ${labels.get(p.data.target!)} : ${formatAmount(p.data.value ?? 0)}`
        return labels.get(p.data.name!) ?? ''
      },
    },
    series: [
      {
        type: 'sankey',
        left: 8,
        right: 120,
        top: 8,
        bottom: 8,
        nodeAlign: 'justify',
        emphasis: { focus: 'adjacency' },
        lineStyle: { color: 'gradient', opacity: 0.4 },
        label: {
          color: colors.text,
          formatter: (params: unknown) => labels.get((params as { name: string }).name) ?? '',
        },
        data: flows.nodes.map((n) => ({
          name: n.id,
          depth: n.column,
          itemStyle: { color: nodeColor(n.id, n.color) },
        })),
        links: flows.links.map((l) => ({ source: l.source, target: l.target, value: l.value })),
      },
    ],
  }
}

/** Clé de période décalée de n mois : chaque mois nomme exactement une période (R1). */
export function shiftPeriodKey(key: string, months: number): string {
  const [year, month] = key.split('-').map(Number)
  const index = year! * 12 + (month! - 1) + months
  return `${String(Math.floor(index / 12)).padStart(4, '0')}-${String((index % 12) + 1).padStart(2, '0')}`
}
