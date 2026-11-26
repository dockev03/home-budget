import { describe, expect, it } from 'vitest'
import { previousPeriodKey } from './cache'

describe('previousPeriodKey', () => {
  it('recule d’un mois, en changeant d’année en janvier', () => {
    expect(previousPeriodKey('2026-11')).toBe('2026-10')
    expect(previousPeriodKey('2026-01')).toBe('2025-12')
  })
})
