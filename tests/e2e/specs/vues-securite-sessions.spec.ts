import { expect, test } from '@playwright/test'
import { login, watchCsp } from './helpers'

test.describe.serial('Sécurité : sessions et alertes', () => {
  test('les sessions ouvertes sont listées et les autres peuvent être fermées', async ({ page }, testInfo) => {
    // Une seule connexion : deux connexions en moins de 30 s réutiliseraient le même code TOTP.
    test.skip(testInfo.project.name !== 'desktop', 'Vérifié une fois.')
    const csp = watchCsp(page)
    await login(page)
    await page.goto('/securite')

    const card = page.getByTestId('sessions')
    const sessions = card.getByTestId('session')
    // Les specs précédentes ont ouvert d'autres sessions.
    await expect(sessions.first()).toBeVisible()
    expect(await sessions.count()).toBeGreaterThan(1)
    const current = sessions.filter({ hasText: 'Cet appareil' })
    await expect(current).toHaveCount(1)
    await expect(current).toContainText('Chrome')
    await expect(current).toContainText('mot de passe + code TOTP')
    await expect(current.getByRole('button', { name: 'Révoquer' })).toHaveCount(0)

    await card.getByRole('button', { name: 'Déconnecter les autres sessions' }).click()
    await card.getByRole('button', { name: 'Confirmer' }).click()
    await expect(card).toContainText('session(s) fermée(s)')
    await expect(sessions).toHaveCount(1)

    // La session courante reste ouverte.
    await page.reload()
    await expect(page.getByRole('heading', { name: 'Sécurité' })).toBeVisible()
    await expect(page.getByTestId('session')).toHaveCount(1)

    // Sans serveur SMTP, les alertes par e-mail sont proposées mais indisponibles.
    const alerts = page.getByTestId('security-alerts')
    await expect(alerts).toContainText('n’est pas configuré')
    await expect(alerts.getByTestId('alert-login').locator('input')).toBeDisabled()

    // L'historique garde les connexions précédentes, avec le navigateur.
    const history = page.getByTestId('login-history').getByTestId('login-entry')
    await expect(history.first()).toContainText('Connexion réussie')
    await expect(history.first()).toContainText('Chrome')
    expect(await history.count()).toBeGreaterThan(1)
    expect(csp).toEqual([])
  })
})
