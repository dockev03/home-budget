/** Clé VAPID base64url → octets, attendus par PushManager.subscribe. */
export function base64UrlToBytes(value: string): Uint8Array<ArrayBuffer> {
  const base64 = value
    .replace(/-/g, '+')
    .replace(/_/g, '/')
    .padEnd(Math.ceil(value.length / 4) * 4, '=')
  const binary = atob(base64)
  const bytes = new Uint8Array(new ArrayBuffer(binary.length))
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i)
  return bytes
}

/** « Firefox · Android », pour reconnaître l'appareil dans la liste. */
export function deviceLabel(userAgent: string): string {
  const browser = /Edg\//.test(userAgent)
    ? 'Edge'
    : /Firefox\//.test(userAgent)
      ? 'Firefox'
      : /Chrome\//.test(userAgent)
        ? 'Chrome'
        : /Safari\//.test(userAgent)
          ? 'Safari'
          : 'Navigateur'
  const system = /Android/.test(userAgent)
    ? 'Android'
    : /iPhone|iPad/.test(userAgent)
      ? 'iOS'
      : /Windows/.test(userAgent)
        ? 'Windows'
        : /Mac OS X/.test(userAgent)
          ? 'macOS'
          : /Linux/.test(userAgent)
            ? 'Linux'
            : null
  return system ? `${browser} · ${system}` : browser
}

export type PushSupport = 'supported' | 'unsupported' | 'ios-not-installed' | 'denied'

/**
 * Le push exige un service worker et l'API Push ; sur iPhone, l'appli doit d'abord être ajoutée à l'écran
 * d'accueil (iOS 16.4 et suivants).
 */
export function pushSupport(env: {
  userAgent: string
  hasServiceWorker: boolean
  hasPushManager: boolean
  standalone: boolean
  permission: NotificationPermission | 'unavailable'
}): PushSupport {
  const ios = /iPhone|iPad/.test(env.userAgent)
  if (ios && !env.standalone) return 'ios-not-installed'
  if (!env.hasServiceWorker || !env.hasPushManager || env.permission === 'unavailable') return 'unsupported'
  if (env.permission === 'denied') return 'denied'
  return 'supported'
}
