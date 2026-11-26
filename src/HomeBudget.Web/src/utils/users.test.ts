import { describe, expect, it } from 'vitest'
import {
  auditSeverity,
  auditTargetLabel,
  authorText,
  formatDateTime,
  invitationLink,
  roleLabel,
  tokenFromHash,
} from './users'

const names = new Map([
  ['a', 'Alice'],
  ['b', 'Bob'],
])

describe('users', () => {
  it('libelle les rôles', () => {
    expect(roleLabel('Admin')).toBe('Administrateur')
    expect(roleLabel('Member')).toBe('Membre')
    expect(roleLabel(undefined)).toBe('Lecture seule')
  })

  it('place le jeton d’invitation après le dièse et le relit', () => {
    const token = 'abcDEF123_-abcDEF123_-abcDEF123_-abcDEF1234'
    const link = invitationLink('https://budget.example', token)
    expect(link).toBe(`https://budget.example/invitation#${token}`)
    expect(tokenFromHash(new URL(link).hash)).toBe(token)
    expect(tokenFromHash('')).toBeNull()
    expect(tokenFromHash('#<script>')).toBeNull()
  })

  it('décrit l’auteur et la dernière modification', () => {
    const created = '2026-11-26T09:00:00Z'
    const updated = '2026-11-27T10:30:00Z'
    expect(
      authorText(
        { createdAt: created, createdBy: 'a', updatedAt: created, updatedBy: 'a', version: 1 },
        names,
      ),
    ).toBe(`Créée par Alice le ${formatDateTime(created)}`)
    expect(
      authorText(
        { createdAt: created, createdBy: 'a', updatedAt: updated, updatedBy: 'b', version: 2 },
        names,
      ),
    ).toBe(`Créée par Alice le ${formatDateTime(created)} · modifiée par Bob le ${formatDateTime(updated)}`)
    expect(
      authorText(
        { createdAt: created, createdBy: null, updatedAt: created, updatedBy: null, version: 1 },
        names,
      ),
    ).toBe(`Créée le ${formatDateTime(created)}`)
    expect(
      authorText({ createdAt: created, createdBy: 'z', updatedAt: created, version: 1 }, names),
    ).toContain('par un ancien utilisateur')
  })

  it('traduit les éléments et signale les événements sensibles du journal', () => {
    expect(auditTargetLabel('Transaction')).toBe('opération')
    expect(auditTargetLabel('Inconnu')).toBe('Inconnu')
    expect(auditTargetLabel(null)).toBe('')
    expect(auditSeverity('LoginFailed')).toBe('error')
    expect(auditSeverity('RoleChanged')).toBe('warning')
    expect(auditSeverity('LoginSucceeded')).toBeUndefined()
  })
})
