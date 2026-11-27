import { describe, expect, it } from 'vitest'
import {
  curlExample,
  daysLeft,
  isExpired,
  scopeLabel,
  webhookEventLabel,
  webhookUrlError,
} from './integrations'

describe('jetons d’accès', () => {
  const now = new Date('2026-11-27T12:00:00Z')

  it('nomme les droits', () => {
    expect(scopeLabel('Read')).toBe('Lecture seule')
    expect(scopeLabel('Write')).toBe('Lecture et écriture')
  })

  it('repère un jeton expiré et compte les jours restants', () => {
    expect(isExpired('2026-11-27T11:59:59Z', now)).toBe(true)
    expect(isExpired('2026-11-28T12:00:00Z', now)).toBe(false)
    expect(daysLeft('2026-12-27T12:00:00Z', now)).toBe(30)
    expect(daysLeft('2026-11-01T00:00:00Z', now)).toBe(0)
  })

  it('propose un exemple d’appel', () => {
    expect(curlExample('https://budget.example', 'hb_x')).toBe(
      'curl -H "Authorization: Bearer hb_x" https://budget.example/api/accounts',
    )
  })
})

describe('webhooks', () => {
  it('traduit les événements, et garde un événement inconnu tel quel', () => {
    expect(webhookEventLabel('transaction.created')).toBe('Opération créée')
    expect(webhookEventLabel('autre.chose')).toBe('autre.chose')
  })

  it('vérifie la forme de l’adresse', () => {
    expect(webhookUrlError('https://n8n.maison.lan/webhook/budget')).toBe(true)
    expect(webhookUrlError('http://192.168.1.20:8123/api/webhook/x')).toBe(true)
    expect(webhookUrlError('ftp://exemple.fr')).toMatch(/http/)
    expect(webhookUrlError('https://moi:secret@exemple.fr')).toMatch(/identifiants/)
    expect(webhookUrlError('exemple.fr')).toMatch(/complète/)
  })
})
