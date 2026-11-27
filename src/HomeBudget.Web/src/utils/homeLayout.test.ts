import { describe, expect, it } from 'vitest'
import { DEFAULT_ORDER, moveBlock, normalizeLayout, toggleHidden, visibleBlocks } from './homeLayout'

describe('normalizeLayout', () => {
  it('applique l’ordre par défaut sans disposition', () => {
    expect(normalizeLayout(null, null)).toEqual({ order: DEFAULT_ORDER, hidden: [] })
  })

  it('ignore les blocs inconnus et les doublons, ajoute les manquants à la fin', () => {
    const layout = normalizeLayout(
      ['accounts', 'removed', 'keyFigures', 'accounts'],
      ['keyFigures', 'removed', 'accounts'],
    )
    expect(layout.order.slice(0, 3)).toEqual(['accounts', 'keyFigures', 'forecast'])
    expect(layout.order).toHaveLength(DEFAULT_ORDER.length)
    expect(layout.hidden).toEqual(['accounts', 'keyFigures'])
  })
})

describe('moveBlock', () => {
  it('échange un bloc avec son voisin', () => {
    expect(moveBlock(['keyFigures', 'forecast', 'charts'], 'charts', -1)).toEqual([
      'keyFigures',
      'charts',
      'forecast',
    ])
    expect(moveBlock(['keyFigures', 'forecast'], 'keyFigures', 1)).toEqual(['forecast', 'keyFigures'])
  })

  it('ne bouge pas en bout de liste', () => {
    expect(moveBlock(['keyFigures', 'forecast'], 'keyFigures', -1)).toEqual(['keyFigures', 'forecast'])
    expect(moveBlock(['keyFigures', 'forecast'], 'forecast', 1)).toEqual(['keyFigures', 'forecast'])
  })
})

describe('blocs masqués', () => {
  it('masque puis réaffiche un bloc, dans l’ordre d’affichage', () => {
    const base = normalizeLayout(['charts', 'keyFigures'], [])
    const masked = toggleHidden(toggleHidden(base, 'keyFigures'), 'charts')
    expect(masked.hidden).toEqual(['charts', 'keyFigures'])
    expect(visibleBlocks(masked)).not.toContain('charts')
    expect(toggleHidden(masked, 'charts').hidden).toEqual(['keyFigures'])
  })
})
