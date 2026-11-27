import { describe, expect, it } from 'vitest'
import { mdiCellphone, mdiHelpCircleOutline, mdiMonitor } from '@mdi/js'
import { deviceIcon, lastSeenLabel, originLabel, sessionTitle } from './sessions'

describe('sessions', () => {
  it('décrit l’appareil', () => {
    expect(sessionTitle({ browser: 'Firefox 133', operatingSystem: 'Windows', device: 'Desktop' })).toBe(
      'Firefox 133 · Windows (ordinateur)',
    )
    expect(sessionTitle({ browser: 'Safari 17', operatingSystem: 'iOS 17', device: 'Mobile' })).toBe(
      'Safari 17 · iOS 17 (mobile)',
    )
    expect(deviceIcon('Mobile')).toBe(mdiCellphone)
    expect(deviceIcon('Desktop')).toBe(mdiMonitor)
    expect(deviceIcon('Unknown')).toBe(mdiHelpCircleOutline)
  })

  it('nomme la méthode de connexion', () => {
    expect(originLabel('Authenticator')).toBe('mot de passe + code TOTP')
    expect(originLabel('Legacy')).toBe('session antérieure au suivi')
  })

  it('donne la dernière activité de façon relative', () => {
    const now = new Date('2026-11-27T12:00:00Z')
    expect(lastSeenLabel('2026-11-27T11:58:00Z', now)).toBe('actif à l’instant')
    expect(lastSeenLabel('2026-11-27T11:48:00Z', now)).toBe('actif il y a 12 min')
    expect(lastSeenLabel('2026-11-27T09:00:00Z', now)).toBe('actif il y a 3 h')
    expect(lastSeenLabel('2026-11-25T11:00:00Z', now)).toBe('actif il y a 2 j')
  })
})
