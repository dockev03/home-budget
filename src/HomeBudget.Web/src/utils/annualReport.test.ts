import { describe, expect, it } from 'vitest'
import { changeColor, changeLabel } from './annualReport'

const nbsp = (text: string) => text.replace(/[\u00a0\u202f]/g, ' ')

describe('annualReport', () => {
  it('décrit l’écart avec l’année précédente', () => {
    expect(nbsp(changeLabel(100, 25))).toBe('+100,00 € (+25 %)')
    expect(nbsp(changeLabel(-20.5, -12.3))).toBe('-20,50 € (-12,3 %)')
    expect(nbsp(changeLabel(50, null))).toBe('+50,00 €')
    expect(changeLabel(0, 0)).toBe('=')
  })

  it('colore selon le sens favorable', () => {
    expect(changeColor('Variable', 10)).toBe('error')
    expect(changeColor('Fixed', -10)).toBe('success')
    expect(changeColor('Income', 10)).toBe('success')
    expect(changeColor('Savings', -10)).toBe('error')
    expect(changeColor('Income', 0)).toBe('medium-emphasis')
  })
})
