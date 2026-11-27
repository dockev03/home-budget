/** API publique (I5) : jetons d'accès personnels et webhooks. */

export type TokenScope = 'Read' | 'Write'

export const TOKEN_DURATIONS = [
  { value: 30, title: '30 jours' },
  { value: 90, title: '90 jours' },
  { value: 365, title: '1 an' },
] as const

export function scopeLabel(scope: TokenScope): string {
  return scope === 'Write' ? 'Lecture et écriture' : 'Lecture seule'
}

/** Un jeton expire à la date indiquée : passé ce moment, il est refusé. */
export function isExpired(expiresAt: string, now: Date = new Date()): boolean {
  return new Date(expiresAt).getTime() <= now.getTime()
}

/** Jours restants avant l'expiration (0 le dernier jour). */
export function daysLeft(expiresAt: string, now: Date = new Date()): number {
  return Math.max(0, Math.floor((new Date(expiresAt).getTime() - now.getTime()) / 86_400_000))
}

export const webhookEventLabels: Record<string, string> = {
  'transaction.created': 'Opération créée',
  'transaction.updated': 'Opération modifiée',
  'transaction.deleted': 'Opération supprimée',
  'import.completed': 'Import terminé',
  ping: 'Test',
}

export function webhookEventLabel(event: string): string {
  return webhookEventLabels[event] ?? event
}

export type DeliveryStatus = 'Pending' | 'Succeeded' | 'Failed'

export const deliveryStatus: Record<DeliveryStatus, { label: string; color: string }> = {
  Pending: { label: 'En attente', color: 'warning' },
  Succeeded: { label: 'Reçu', color: 'success' },
  Failed: { label: 'Échec', color: 'error' },
}

/** Contrôle rapide de l'adresse avant l'envoi ; le serveur vérifie aussi les adresses interdites. */
export function webhookUrlError(url: string): string | true {
  try {
    const parsed = new URL(url.trim())
    if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:')
      return 'Adresse http:// ou https:// attendue.'
    if (parsed.username || parsed.password) return 'Pas d’identifiants dans l’adresse.'
    return true
  } catch {
    return 'Adresse complète attendue (https://…).'
  }
}

/** Exemple d'appel affiché sous un jeton tout juste créé. */
export function curlExample(origin: string, token: string): string {
  return `curl -H "Authorization: Bearer ${token}" ${origin}/api/accounts`
}
