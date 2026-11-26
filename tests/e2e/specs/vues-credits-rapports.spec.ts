import { expect, test, type Page } from '@playwright/test'
import { login, watchCsp } from './helpers'

/** Choisit une valeur dans un v-select du dialogue ouvert, à partir de son libellé. */
async function pick(page: Page, label: string, option: string) {
  await page
    .getByRole('dialog')
    .getByRole('combobox', { name: label })
    .first()
    .locator('xpath=ancestor::div[contains(@class, "v-field ")][1]')
    .click()
  await page.getByRole('listbox', { name: label }).getByRole('option', { name: option, exact: true }).click()
}

const menu = (page: Page, title: string) => page.locator('.v-navigation-drawer').getByText(title, { exact: true }).click()

// Exécuté en dernier (ordre alphabétique) : les comptes, opérations et récurrences des parcours précédents existent.
test.describe.serial('crédits, calendrier, rapports et répartition', () => {
  test('crédit, calendrier, rapport enregistré, flux et répartition', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Les données sont créées une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    // Crédit (A8) : conditions saisies, synthèse et tableau d'amortissement.
    await menu(page, 'Comptes')
    await page.getByRole('button', { name: 'Nouveau compte' }).click()
    await page.getByLabel('Nom').fill('Prêt auto')
    await pick(page, 'Type', 'Crédit')
    await page.getByLabel('Solde initial').fill('-12 000')
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByRole('dialog')).toBeHidden()
    await page.getByRole('button', { name: 'Crédit Prêt auto' }).click()
    const loan = page.getByTestId('loan')
    await loan.getByLabel('Capital emprunté').fill('12 000')
    await loan.getByLabel('Taux annuel').fill('3,875')
    await loan.getByLabel('Durée').fill('48')
    await loan.getByLabel('Assurance par mois').fill('6,50')
    await loan.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(loan.getByTestId('loan-payment')).toContainText('276,78')
    await expect(loan.getByTestId('loan-schedule').locator('tbody tr')).toHaveCount(12)
    await loan.getByRole('button', { name: /Tout le tableau/ }).click()
    await expect(loan.getByTestId('loan-schedule').locator('tbody tr')).toHaveCount(48)
    await loan.getByRole('button', { name: 'Fermer' }).click()
    await expect(page.locator('.v-list-item').filter({ hasText: 'Prêt auto' })).toContainText('Capital restant dû')

    // Calendrier des échéances (E8).
    await menu(page, 'Calendrier')
    await expect(page.getByRole('heading', { name: 'Calendrier des échéances' })).toBeVisible()
    await expect(page.getByTestId('calendar-grid')).toBeVisible()
    await expect(page.getByTestId('calendar-expenses')).toContainText('€')

    // Rapport personnalisé (G8), enregistré, puis flux (G9).
    await menu(page, 'Rapports')
    await expect(page.getByTestId('report-result')).toBeVisible()
    await page.getByTestId('report-form').getByRole('combobox', { name: 'Regrouper par' })
      .locator('xpath=ancestor::div[contains(@class, "v-field ")][1]').click()
    await page.getByRole('listbox', { name: 'Regrouper par' }).getByRole('option', { name: 'Tiers', exact: true }).click()
    await page.getByTestId('run-report').click()
    await expect(page.getByTestId('report-result').locator('thead')).toContainText('Tiers')
    await page.getByTestId('report-name').getByRole('textbox').fill('Par tiers')
    await page.getByTestId('save-report').click()
    await expect(page.getByRole('button', { name: 'Mettre à jour' })).toBeVisible()
    await page.getByRole('tab', { name: 'Flux' }).click()
    await expect(page.getByTestId('flows')).toBeVisible()

    // Répartition des dépenses communes (K4).
    await menu(page, 'Répartition')
    await expect(page.getByRole('heading', { name: 'Répartition des dépenses' })).toBeVisible()
    await expect(page.getByTestId('split-common')).toContainText('€')
    await expect(page.getByTestId('settlements')).toBeVisible()
    expect(csp).toEqual([])
  })

  test('calendrier et répartition sur mobile', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'mobile', 'Parcours mobile.')
    await login(page)
    await page.goto('/calendrier')
    await expect(page.getByTestId('calendar-grid')).toBeVisible()
    await page.goto('/rapports')
    await expect(page.getByTestId('report-result')).toBeVisible()
    await page.goto('/repartition')
    await expect(page.getByTestId('settlements')).toBeVisible()
  })
})
