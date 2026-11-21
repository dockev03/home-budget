import { beforeEach, describe, expect, it } from 'vitest'
import { loadThemePreference, resolveTheme, saveThemePreference } from './themePreference'

describe('resolveTheme', () => {
  it('suit le système', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
  })

  it('respecte un choix explicite', () => {
    expect(resolveTheme('dark', false)).toBe('dark')
    expect(resolveTheme('light', true)).toBe('light')
  })
})

describe('préférence mémorisée', () => {
  beforeEach(() => localStorage.clear())

  it('vaut « system » par défaut', () => {
    expect(loadThemePreference()).toBe('system')
  })

  it('est relue après enregistrement', () => {
    saveThemePreference('dark')
    expect(loadThemePreference()).toBe('dark')
  })
})
