import { describe, expect, it } from 'vitest'
import { describeChange } from './transactionHistory'

const nbsp = (text: string) => text.replace(/[\u00a0\u202f]/g, ' ')

describe('transactionHistory', () => {
  it('décrit un champ modifié', () => {
    expect(
      describeChange({
        field: 'account',
        before: 'Courant',
        after: 'Joint',
        beforeLines: null,
        afterLines: null,
      }),
    ).toBe('Compte : Courant → Joint')
    expect(
      describeChange({
        field: 'date',
        before: '2026-03-01',
        after: '2026-03-02',
        beforeLines: null,
        afterLines: null,
      }),
    ).toBe('Date : 01/03/2026 → 02/03/2026')
    expect(
      describeChange({
        field: 'cleared',
        before: 'false',
        after: 'true',
        beforeLines: null,
        afterLines: null,
      }),
    ).toBe('Pointage : non pointée → pointée')
    expect(
      describeChange({ field: 'notes', before: null, after: 'Ticket', beforeLines: null, afterLines: null }),
    ).toBe('Note : vide → Ticket')
  })

  it('décrit la ventilation avant et après', () => {
    const text = describeChange({
      field: 'lines',
      before: null,
      after: null,
      beforeLines: [{ category: null, amount: -10, memo: null, tags: [] }],
      afterLines: [
        { category: 'Courses', amount: -6, memo: 'Pain', tags: ['Maison'] },
        { category: 'Restaurants', amount: -4, memo: null, tags: [] },
      ],
    })
    expect(nbsp(text)).toBe(
      'Ventilation : Sans catégorie -10,00 € → Courses -6,00 € « Pain » (Maison) + Restaurants -4,00 €',
    )
  })
})
