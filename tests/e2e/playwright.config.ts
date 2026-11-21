import { defineConfig, devices } from '@playwright/test'

/**
 * Tests de bout en bout contre une instance démarrée à part (conteneur Docker en CI) :
 *   E2E_BASE_URL     URL de l'application (défaut http://localhost:8080)
 *   E2E_SETUP_TOKEN  jeton de premier lancement imposé via HB__SetupToken
 * L'instance doit partir d'un volume /data vide.
 */
export default defineConfig({
  testDir: './specs',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:8080',
    trace: 'off',
    locale: 'fr-FR',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'mobile', use: { ...devices['Pixel 7'] }, dependencies: ['desktop'] },
  ],
})
