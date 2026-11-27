/** Sessions ouvertes (N8) : libellés et mise en forme. */
import { mdiCellphone, mdiHelpCircleOutline, mdiMonitor, mdiTablet } from '@mdi/js'

export type DeviceKind = 'Unknown' | 'Desktop' | 'Mobile' | 'Tablet'
export type SessionOrigin = 'Password' | 'Authenticator' | 'RecoveryCode' | 'Legacy'

const deviceIcons: Record<DeviceKind, string> = {
  Unknown: mdiHelpCircleOutline,
  Desktop: mdiMonitor,
  Mobile: mdiCellphone,
  Tablet: mdiTablet,
}

const deviceLabels: Record<DeviceKind, string> = {
  Unknown: 'appareil inconnu',
  Desktop: 'ordinateur',
  Mobile: 'mobile',
  Tablet: 'tablette',
}

const originLabels: Record<SessionOrigin, string> = {
  Password: 'mot de passe',
  Authenticator: 'mot de passe + code TOTP',
  RecoveryCode: 'mot de passe + code de secours',
  Legacy: 'session antérieure au suivi',
}

export function deviceIcon(device: DeviceKind): string {
  return deviceIcons[device] ?? mdiHelpCircleOutline
}

/** « Firefox 133 · Windows (ordinateur) ». */
export function sessionTitle(session: {
  browser: string
  operatingSystem: string
  device: DeviceKind
}): string {
  return `${session.browser} · ${session.operatingSystem} (${deviceLabels[session.device] ?? deviceLabels.Unknown})`
}

export function originLabel(origin: SessionOrigin): string {
  return originLabels[origin] ?? origin
}

/** Dernière activité relative : « à l'instant », « il y a 12 min », « il y a 3 h », « il y a 2 j ». */
export function lastSeenLabel(iso: string, now: Date = new Date()): string {
  const minutes = Math.floor((now.getTime() - new Date(iso).getTime()) / 60_000)
  // La dernière activité est enregistrée à 5 minutes près.
  if (minutes < 5) return 'actif à l’instant'
  if (minutes < 60) return `actif il y a ${minutes} min`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `actif il y a ${hours} h`
  return `actif il y a ${Math.floor(hours / 24)} j`
}

/** Appareil d'une entrée du journal, ou null si le navigateur n'a pas été enregistré (entrées anciennes). */
export function agentLabel(entry: {
  browser?: string | null
  operatingSystem?: string | null
  device?: DeviceKind | null
}): string | null {
  if (!entry.browser) return null
  return sessionTitle({
    browser: entry.browser,
    operatingSystem: entry.operatingSystem ?? 'Inconnu',
    device: entry.device ?? 'Unknown',
  })
}
