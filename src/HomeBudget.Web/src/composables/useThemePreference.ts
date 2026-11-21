import { ref, watch } from 'vue'
import { useTheme } from 'vuetify'
import {
  loadThemePreference,
  resolveTheme,
  saveThemePreference,
  type ThemePreference,
} from './themePreference'

// État partagé par toute l'application (une seule préférence, un seul écouteur système).
const preference = ref<ThemePreference>(loadThemePreference())
let initialized = false

/** Thème clair, sombre ou automatique (suit le système), appliqué à Vuetify. */
export function useThemePreference() {
  if (!initialized) {
    initialized = true
    const theme = useTheme()
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const apply = () => theme.change(resolveTheme(preference.value, media.matches))
    media.addEventListener('change', apply)
    watch(
      preference,
      (value) => {
        saveThemePreference(value)
        apply()
      },
      { immediate: true },
    )
  }
  return { preference }
}
