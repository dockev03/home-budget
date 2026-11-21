import 'vuetify/styles'
import { createVuetify } from 'vuetify'
import { aliases, mdi } from 'vuetify/iconsets/mdi-svg'
import { fr } from 'vuetify/locale'

export const vuetify = createVuetify({
  locale: { locale: 'fr', messages: { fr } },
  icons: { defaultSet: 'mdi', aliases, sets: { mdi } },
  theme: {
    defaultTheme: 'light',
    themes: {
      light: { colors: { primary: '#1e5aa8', secondary: '#4a3aa5' } },
      dark: { colors: { primary: '#7ab0ff', secondary: '#b3a8ff' } },
    },
  },
  defaults: {
    VTextField: { variant: 'outlined', density: 'comfortable' },
    VBtn: { rounded: 'lg' },
    VCard: { rounded: 'lg' },
  },
})
