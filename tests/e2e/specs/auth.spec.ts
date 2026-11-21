import { expect, test } from '@playwright/test'
import { totp } from './totp'
import { login, password, saveKey, setupToken, userName, watchCsp } from './helpers'

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
