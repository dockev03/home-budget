import { expect, test } from '@playwright/test'
import { login, watchCsp } from './helpers'

// Exécuté après « opérations » (ordre alphabétique) : le compte joint, inclus dans la prévision, existe déjà.
test.describe.serial('prévision avancée, scénarios et comparaison', () => {
  test('scénario « et si… », horizon de 6 mois et comparaison', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Les données sont créées une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    // Scénario actif avec un événement mensuel (F5).
    await page.locator('.v-navigation-drawer').getByText('Scénarios', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Scénarios' })).toBeVisible()
    await page.getByRole('button', { name: 'Nouveau scénario' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Nom').fill('Nouvelle voiture')
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog).toBeHidden()
    const scenario = page.getByTestId('scenario').filter({ hasText: 'Nouvelle voiture' })
    await scenario.getByRole('button', { name: 'Ajouter un événement' }).click()
    await dialog.getByLabel('Libellé').fill('Crédit auto')
    await dialog.getByLabel('Montant', { exact: true }).fill('250')
    await dialog.getByLabel('Chaque mois').check()
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog).toBeHidden()
    await expect(scenario).toContainText('Crédit auto')
    await expect(scenario).toContainText('Chaque mois dès le')
    await expect(scenario).toContainText('-250,00')

    // Tableau de bord : le scénario actif est ajouté à la courbe, à part (F5).
    await page.locator('.v-navigation-drawer').getByText('Accueil', { exact: true }).click()
    const forecast = page.getByTestId('forecast')
    await expect(forecast.getByTestId('scenario-chips')).toContainText('Nouvelle voiture')
    await expect(forecast.getByTestId('scenario-impact')).toContainText('Avec les scénarios')

    // Horizon de 6 mois (F4) : solde prévu en fin de chaque mois.
    await forecast.getByTestId('horizon').getByRole('button', { name: '6 mois' }).click()
    await expect(forecast).toContainText('Prévision sur 6 mois')
    await forecast.getByRole('button', { name: 'Solde prévu en fin de mois' }).click()
    await expect(forecast.getByTestId('period-ends').locator('tbody tr')).toHaveCount(6)
    await expect(forecast.getByTestId('period-ends')).toContainText('Avec scénarios')

    // Sans le scénario, la série à part disparaît.
    await forecast.getByTestId('scenario-chips').getByText('Nouvelle voiture').click()
    await expect(forecast.getByTestId('scenario-impact')).toHaveCount(0)

    // Comparaison avec le mois précédent, puis un an avant (G6).
    const comparison = page.getByTestId('comparison')
    await expect(comparison).toContainText('Dépenses variables')
    await comparison.getByRole('button', { name: 'Un an avant' }).click()
    await expect(comparison).toContainText('Revenus')
    expect(csp).toEqual([])
  })

  test('stratégie de prévision par la moyenne', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Parcours PC.')
    await login(page)
    await page.goto('/parametres')
    await page.getByLabel(/Par la moyenne/).check()
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByText('Paramètres enregistrés.')).toBeVisible()
    await page.goto('/')
    await expect(page.getByTestId('forecast')).toContainText('par la moyenne des 6 derniers mois')

    // Retour à la stratégie par le budget, attendue par les parcours suivants.
    await page.goto('/parametres')
    await page.getByLabel(/Par le budget/).check()
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByText('Paramètres enregistrés.')).toBeVisible()
  })

  test('scénarios sur mobile', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'mobile', 'Parcours mobile.')
    await login(page)
    await page.goto('/scenarios')
    await expect(page.getByRole('heading', { name: 'Scénarios' })).toBeVisible()
    await expect(page.getByTestId('scenario').filter({ hasText: 'Nouvelle voiture' })).toContainText('Crédit auto')
  })
})
