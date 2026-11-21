/** Formats français (L7) : montants « 1 234,56 € », dates « JJ/MM/AAAA ». */

const currency = new Intl.NumberFormat('fr-FR', { style: 'currency', currency: 'EUR' })
const dayHeading = new Intl.DateTimeFormat('fr-FR', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  year: 'numeric',
})

export function formatAmount(value: number): string {
  return currency.format(value)
}

/** « +12,50 € » pour une entrée, « -12,50 € » pour une sortie. */
export function formatSignedAmount(value: number): string {
  return value > 0 ? `+${currency.format(value)}` : currency.format(value)
}

/** Date ISO (AAAA-MM-JJ) → JJ/MM/AAAA, sans passer par un fuseau horaire. */
export function formatDate(iso: string): string {
  const [year, month, day] = iso.split('-')
  return `${day}/${month}/${year}`
}

/** Date ISO → « samedi 21 novembre 2026 ». */
export function formatDayHeading(iso: string): string {
  const text = dayHeading.format(fromIso(iso))
  return text.charAt(0).toUpperCase() + text.slice(1)
}

export function fromIso(iso: string): Date {
  const [year, month, day] = iso.split('-').map(Number)
  return new Date(year!, month! - 1, day!)
}

export function toIso(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

export function todayIso(): string {
  return toIso(new Date())
}

/**
 * Lit un montant saisi à la française ou non : « 1 234,56 », « -12.5 », « 12,50 € ».
 * Renvoie null si le texte n'est pas un montant à 2 décimales au plus.
 */
export function parseAmount(text: string | number | null | undefined): number | null {
  if (typeof text === 'number') return Number.isFinite(text) ? text : null
  if (text === null || text === undefined) return null
  const cleaned = text.replace(/[\s  €]/g, '').replace(',', '.')
  if (!/^[+-]?\d+(\.\d{1,2})?$/.test(cleaned)) return null
  return Number(cleaned)
}

/** Arrondi au centime, pour éviter les écarts de virgule flottante (0,1 + 0,2). */
export function roundCents(value: number): number {
  return Math.round(value * 100) / 100
}
