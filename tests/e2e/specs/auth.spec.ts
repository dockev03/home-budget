import { expect, test, type Page } from '@playwright/test'
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { totp } from './totp'

const setupToken = process.env.E2E_SETUP_TOKEN ?? 'e2e-setup-token'
const userName = 'admin'
const password = 'une phrase de passe solide'
// La clé TOTP est partagée entre projets (desktop puis mobile), exécutés dans des workers distincts.
const stateFile = new URL('../test-results/e2e-state.json', import.meta.url)
const saveKey = (key: string) => writeFileSync(stateFile, JSON.stringify({ key }))
const loadKey = (): string => (existsSync(stateFile) ? JSON.parse(readFileSync(stateFile, 'utf8')).key : '')

/** Échoue si la politique CSP bloque une ressource (script, style, image…). */
function watchCsp(page: Page): string[] {
  const violations: string[] = []
  page.on('console', (msg) => {
    if (msg.type() === 'error' && /Content Security Policy/i.test(msg.text())) violations.push(msg.text())
  })
  return violations
}

async function login(page: Page) {
  await page.goto('/')
  await expect(page).toHaveURL(/\/connexion/)
  await page.getByLabel('Identifiant').fill(userName)
  await page.getByLabel('Mot de passe', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Se connecter' }).click()
  await expect(page.getByText('Double authentification')).toBeVisible()
  await page.getByLabel('Code', { exact: true }).fill(totp(loadKey()))
  await page.getByRole('button', { name: 'Valider' }).click()
  await expect(page.getByRole('heading', { name: `Bonjour ${userName}` })).toBeVisible()
}

test.describe.serial('authentification', () => {
  test('premier lancement puis activation de la double authentification', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Le premier lancement ne se fait qu’une fois.')
    const csp = watchCsp(page)

    await page.goto('/')
    await expect(page).toHaveURL(/\/premier-lancement/)
    await page.getByLabel('Jeton de premier lancement').fill(setupToken)
    await page.getByLabel('Identifiant').fill(userName)
    await page.getByLabel('Mot de passe', { exact: true }).fill(password)
    await page.getByLabel('Confirmation du mot de passe').fill(password)
    await page.getByRole('button', { name: 'Créer le compte administrateur' }).click()

    await expect(page).toHaveURL(/\/securite/)
    await page.getByRole('button', { name: 'Activer' }).click()
    await expect(page.getByAltText('QR code de configuration')).toBeVisible()
    const sharedKey = (await page.locator('code').first().textContent()) ?? ''
    expect(sharedKey).not.toBe('')
    saveKey(sharedKey)

    await page.getByLabel('Code de vérification').fill(totp(sharedKey))
    await page.getByRole('button', { name: "Confirmer l'activation" }).click()
    await expect(page.getByText('Codes de secours')).toBeVisible()
    await expect(page.getByText(/Il vous reste\s+10\s+code/)).toBeVisible()

    await page.getByRole('button', { name: 'Compte' }).click()
    await page.getByText('Se déconnecter').click()
    await expect(page).toHaveURL(/\/connexion/)

    await login(page)
    expect(csp).toEqual([])
  })

  test('connexion avec double authentification et navigation', async ({ page }, testInfo) => {
    const csp = watchCsp(page)
    await login(page)

    if (testInfo.project.name === 'mobile') {
      await page.locator('.v-bottom-navigation').getByText('Sécurité').click()
    } else {
      await page.locator('.v-navigation-drawer').getByText('Sécurité').click()
    }
    await expect(page.getByRole('heading', { name: 'Sécurité' })).toBeVisible()
    await expect(page.getByText(/Active\. Il vous reste/)).toBeVisible()
    expect(csp).toEqual([])
  })

  test('une page protégée renvoie vers la connexion', async ({ page }) => {
    await page.goto('/securite')
    await expect(page).toHaveURL(/\/connexion\?redirect=(%2F|\/)securite/)
  })
})
