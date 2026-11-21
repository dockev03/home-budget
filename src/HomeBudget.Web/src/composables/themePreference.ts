export type ThemePreference = 'light' | 'dark' | 'system'
export type ThemeName = 'light' | 'dark'

const STORAGE_KEY = 'hb.theme'

export function resolveTheme(preference: ThemePreference, prefersDark: boolean): ThemeName {
  if (preference === 'system') return prefersDark ? 'dark' : 'light'
  return preference
}

/** Préférence mémorisée dans le navigateur (stockage local, facultatif). */
export function loadThemePreference(): ThemePreference {
  try {
    const value = localStorage.getItem(STORAGE_KEY)
    return value === 'light' || value === 'dark' || value === 'system' ? value : 'system'
  } catch {
    return 'system'
  }
}

export function saveThemePreference(preference: ThemePreference): void {
  try {
    localStorage.setItem(STORAGE_KEY, preference)
  } catch {
    // Stockage indisponible (navigation privée) : la préférence n'est pas mémorisée.
  }
}
