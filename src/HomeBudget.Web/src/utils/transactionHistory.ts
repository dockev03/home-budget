import type { Schemas } from '@/api/client'
import { periodLabel } from './budget'
import { formatAmount, formatDate } from './format'

type FieldChange = Schemas['FieldChangeDto']
type LineChange = Schemas['LineChangeDto']

const fieldTitles: Record<string, string> = {
  date: 'Date',
  account: 'Compte',
  payee: 'Tiers',
  label: 'Libellé',
  notes: 'Note',
  cleared: 'Pointage',
  reconciled: 'Rapprochement',
  reimburses: 'Dépense remboursée',
  advance: 'Avance',
  referenceMonth: 'Mois concerné',
  lines: 'Ventilation',
}

function value(field: string, raw: string | null): string {
  if (raw === null || raw === '') return 'vide'
  switch (field) {
    case 'date':
      return formatDate(raw)
    case 'referenceMonth':
      return periodLabel(raw)
    case 'cleared':
      return raw === 'true' ? 'pointée' : 'non pointée'
    case 'reconciled':
      return raw === 'true' ? 'rapprochée' : 'non rapprochée'
    default:
      return raw
  }
}

/** « Courses 42,50 € (Vacances) » ; plusieurs lignes séparées par « + ». */
export function describeLines(lines: LineChange[] | null): string {
  if (!lines?.length) return 'vide'
  return lines
    .map((l) => {
      const tags = l.tags.length ? ` (${l.tags.join(', ')})` : ''
      const memo = l.memo ? ` « ${l.memo} »` : ''
      return `${l.category ?? 'Sans catégorie'} ${formatAmount(l.amount)}${memo}${tags}`
    })
    .join(' + ')
}

/** Un champ modifié en une ligne : « Compte : Courant → Joint ». */
export function describeChange(change: FieldChange): string {
  const title = fieldTitles[change.field] ?? change.field
  if (change.field === 'lines') {
    return `${title} : ${describeLines(change.beforeLines)} → ${describeLines(change.afterLines)}`
  }
  return `${title} : ${value(change.field, change.before)} → ${value(change.field, change.after)}`
}
