import { describe, expect, it } from 'vitest'
import { base64UrlToBytes, deviceLabel, pushSupport } from './push'

describe('base64UrlToBytes', () => {
  it('décode le base64url sans remplissage', () => {
    expect([...base64UrlToBytes('-_8')]).toEqual([0xfb, 0xff])
    expect(
      base64UrlToBytes(
        'BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4',
      ),
    ).toHaveLength(65)
  })
})

describe('deviceLabel', () => {
  it('reconnaît le navigateur et le système', () => {
    expect(
      deviceLabel('Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/131.0 Mobile Safari/537.36'),
    ).toBe('Chrome · Android')
    expect(
      deviceLabel(
        'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1',
      ),
    ).toBe('Safari · iOS')
    expect(
      deviceLabel('Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0'),
    ).toBe('Firefox · Windows')
    expect(
      deviceLabel('Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 Chrome/131.0 Safari/537.36 Edg/131.0'),
    ).toBe('Edge · Windows')
  })
})

describe('pushSupport', () => {
  const base = {
    userAgent: 'Mozilla/5.0 (Linux; Android 14) Chrome/131.0',
    hasServiceWorker: true,
    hasPushManager: true,
    standalone: false,
    permission: 'default' as const,
  }

  it('exige l’installation sur iPhone', () => {
    expect(
      pushSupport({ ...base, userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)' }),
    ).toBe('ios-not-installed')
    expect(
      pushSupport({
        ...base,
        userAgent: 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)',
        standalone: true,
      }),
    ).toBe('supported')
  })

  it('signale un navigateur sans push ou une permission refusée', () => {
    expect(pushSupport({ ...base, hasPushManager: false })).toBe('unsupported')
    expect(pushSupport({ ...base, permission: 'denied' })).toBe('denied')
    expect(pushSupport(base)).toBe('supported')
  })
})
