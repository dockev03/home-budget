/** Utilisateurs du foyer (K1, K5), auteur des opérations (K2) et journal d'audit (N6). */

export type Role = 'Admin' | 'Member' | 'ReadOnly'

export const roles: { value: Role; title: string; description: string }[] = [
  {
    value: 'Admin',
    title: 'Administrateur',
    description: 'Tout, y compris les utilisateurs et les sauvegardes.',
  },
  { value: 'Member', title: 'Membre', description: 'Consulte et saisit.' },
  { value: 'ReadOnly', title: 'Lecture seule', description: 'Consulte sans rien modifier.' },
]

export function roleLabel(role: string | undefined): string {
  return roles.find((r) => r.value === role)?.title ?? 'Lecture seule'
}

/** Le jeton est placé après « # » : il n'est ni envoyé au serveur, ni écrit dans les journaux du proxy. */
export function invitationLink(origin: string, token: string): string {
  return `${origin}/invitation#${encodeURIComponent(token)}`
}

export function tokenFromHash(hash: string): string | null {
  const token = decodeURIComponent(hash.replace(/^#/, '')).trim()
  return /^[A-Za-z0-9_-]{20,100}$/.test(token) ? token : null
}

const dateTime = new Intl.DateTimeFormat('fr-FR', { dateStyle: 'short', timeStyle: 'short' })

/** Horodatage ISO → « 26/11/2026 14:05 » (heure locale). */
export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso))
}

export interface Traced {
  createdAt: string
  createdBy?: string | null
  updatedAt: string
  updatedBy?: string | null
  version: number
}

/**
 * « Créée par Alice le 26/11/2026 14:05 · modifiée par Bob le 27/11/2026 09:00 » (K2).
 * Sans auteur connu (génération automatique, import hors session), « Créée le … ».
 */
export function authorText(item: Traced, names: ReadonlyMap<string, string>): string {
  const who = (id: string | null | undefined) => (id ? (names.get(id) ?? 'un ancien utilisateur') : null)
  const created = who(item.createdBy)
  const parts = [`Créée${created ? ` par ${created}` : ''} le ${formatDateTime(item.createdAt)}`]
  if (item.version > 1 && item.updatedAt !== item.createdAt) {
    const updated = who(item.updatedBy)
    parts.push(`modifiée${updated ? ` par ${updated}` : ''} le ${formatDateTime(item.updatedAt)}`)
  }
  return parts.join(' · ')
}

export const auditActions: Record<string, string> = {
  LoginSucceeded: 'Connexion',
  LoginFailed: 'Échec de connexion',
  LoginLockedOut: 'Compte verrouillé',
  PasswordChanged: 'Mot de passe changé',
  TwoFactorEnabled: 'Double authentification activée',
  TwoFactorDisabled: 'Double authentification désactivée',
  UserInvited: 'Invitation envoyée',
  InvitationRevoked: 'Invitation révoquée',
  InvitationAccepted: 'Invitation acceptée',
  RoleChanged: 'Rôle changé',
  UserDeactivated: 'Utilisateur désactivé',
  UserReactivated: 'Utilisateur réactivé',
  AccountVisibilityChanged: 'Visibilité d’un compte',
  Exported: 'Export des opérations',
  BackupExported: 'Sauvegarde téléchargée',
  Restored: 'Restauration',
  Deleted: 'Suppression',
  ApiTokenCreated: 'Jeton d’accès créé',
  ApiTokenRevoked: 'Jeton d’accès révoqué',
  WebhookCreated: 'Webhook créé',
  WebhookUpdated: 'Webhook modifié',
  WebhookDisabled: 'Webhook désactivé (échecs)',
  AttachmentsRestored: 'Pièces jointes restaurées',
  SessionRevoked: 'Session fermée à distance',
}

const targets: Record<string, string> = {
  Account: 'compte',
  Advance: 'avance',
  Attachment: 'pièce jointe',
  Category: 'catégorie',
  CategoryBudget: 'budget',
  ImportProfile: 'profil d’import',
  Invitation: 'invitation',
  LabelRule: 'règle apprise',
  Reconciliation: 'rapprochement',
  Recurrence: 'récurrence',
  Transaction: 'opération',
  User: 'utilisateur',
  UserSession: 'session',
  Webhook: 'webhook',
}

export function auditTargetLabel(target: string | null | undefined): string {
  return target ? (targets[target] ?? target) : ''
}

/** Actions signalées en couleur dans le journal : échecs et changements de droits. */
export function auditSeverity(action: string): 'error' | 'warning' | undefined {
  if (action === 'LoginFailed' || action === 'LoginLockedOut') return 'error'
  if (
    [
      'RoleChanged',
      'UserDeactivated',
      'TwoFactorDisabled',
      'SessionRevoked',
      'Restored',
      'AccountVisibilityChanged',
      'WebhookDisabled',
    ].includes(action)
  )
    return 'warning'
  return undefined
}
