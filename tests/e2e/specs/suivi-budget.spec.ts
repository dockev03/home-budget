import { expect, test, type Page } from '@playwright/test'
import { login, watchCsp } from './helpers'

/** Choisit une valeur dans un v-autocomplete du dialogue ouvert, en tapant le début du texte. */
async function search(page: Page, label: string, option: string) {
  const field = page.getByRole('dialog').getByRole('combobox', { name: label })
  await field.locator('xpath=ancestor::div[contains(@class, "v-field ")][1]').click()
  await field.pressSequentially(option)
  await page
    .getByRole('listbox', { name: label })
    .getByRole('option')
    .filter({ hasText: new RegExp(`^${option}`) })
    .first()
    .click()
}

test.describe.serial('budget avancé, provisions, bilan annuel et historique', () => {
  test('budget propre à la période, copie, provision et bilan annuel', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Les données sont créées une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    await page.locator('.v-navigation-drawer').getByText('Budget', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Budget' })).toBeVisible()

    // Budget de l'essence pour cette seule période (D4).
    await page.getByRole('listitem', { name: 'Budget Essence' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel(/seulement$/).check()
    await dialog.getByRole('button', { name: 'Montant' }).click()
    await dialog.getByLabel('Montant par période').fill('120')
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog).toBeHidden()
    const essence = page.getByRole('listitem', { name: 'Budget Essence' })
    await expect(essence).toContainText('cette période')
    await expect(essence).toContainText('120,00')

    // Période suivante : copie depuis la période précédente, avec aperçu (D5).
    await page.getByRole('button', { name: 'Période suivante' }).click()
    await expect(page.getByRole('listitem', { name: 'Budget Essence' })).not.toContainText('cette période')
    await page.getByRole('button', { name: 'Copier' }).click()
    await expect(dialog.getByText('Essence')).toBeVisible()
    await dialog.getByRole('button', { name: /^Copier \d+ budget/ }).click()
    await expect(dialog).toBeHidden()
    await expect(page.getByRole('listitem', { name: 'Budget Essence' })).toContainText('120,00')
    await page.getByRole('button', { name: 'Période en cours' }).click()

    // Provision annuelle (D11).
    const provisions = page.getByTestId('provisions')
    await provisions.getByRole('button', { name: 'Ajouter' }).click()
    await dialog.getByLabel('Libellé').fill('Taxe foncière')
    await search(page, 'Catégorie de dépense', 'Impôts')
    await dialog.getByLabel('Montant', { exact: true }).fill('1 200')
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog).toBeHidden()
    await expect(provisions).toContainText('Taxe foncière')
    await expect(page.getByRole('listitem', { name: 'Budget Impôts' })).toContainText('de provisions')

    // Bilan annuel (P3).
    await page.locator('.v-navigation-drawer').getByText('Bilan annuel', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Bilan annuel' })).toBeVisible()
    await expect(page.getByText('Récapitulatif', { exact: true })).toBeVisible()
    await expect(page.getByTestId('annual-Courses')).toBeVisible()
    await page.getByRole('button', { name: 'Année précédente' }).click()
    await expect(page).toHaveURL(/\/bilan\/\d{4}$/)
    expect(csp).toEqual([])
  })

  test('historique des modifications d’une opération', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Parcours PC.')
    await login(page)

    await page.locator('.v-navigation-drawer').getByText('Opérations', { exact: true }).click()
    // La dépense Leclerc a été pointée après sa création (parcours « opérations »).
    await page.locator('.v-list-item', { hasText: 'Leclerc' }).first().click()
    const history = page.getByTestId('history')
    await history.getByRole('button', { name: 'Historique des modifications' }).click()
    await expect(history).toContainText('Pointage : non pointée → pointée')
    await expect(history).toContainText('admin')
  })

  test('bilan annuel sur mobile', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'mobile', 'Parcours mobile.')
    await login(page)
    await page.goto('/bilan')
    await expect(page.getByRole('heading', { name: 'Bilan annuel' })).toBeVisible()
    await expect(page.getByTestId('annual-Courses')).toBeVisible()
  })
})
