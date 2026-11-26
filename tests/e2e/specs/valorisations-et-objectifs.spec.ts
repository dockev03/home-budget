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

const iso = (date: Date) =>
  `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`

// Exécuté après « opérations » (ordre alphabétique) : le Livret A, compte d'épargne, existe déjà.
test.describe.serial('valorisations, patrimoine et objectifs d’épargne', () => {
  test('valorisation, objectif et patrimoine sur le tableau de bord', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Les données sont créées une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    // Compte d'investissement et sa valorisation (A7).
    await page.locator('.v-navigation-drawer').getByText('Comptes', { exact: true }).click()
    await page.getByRole('button', { name: 'Nouveau compte' }).click()
    await page.getByLabel('Nom').fill('PEA')
    await pick(page, 'Type', 'Investissement')
    await page.getByLabel('Solde initial').fill('1 000,00')
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByRole('dialog')).toBeHidden()
    await page.getByRole('button', { name: 'Valorisations de PEA' }).click()
    const valuations = page.getByTestId('valuations')
    await valuations.getByLabel('Valeur').fill('1 250,40')
    await valuations.getByLabel('Note (facultative)').fill('Relevé du courtier')
    await valuations.getByRole('button', { name: 'Ajouter la valeur' }).click()
    await expect(valuations.getByRole('listitem')).toContainText('1 250,40')
    await valuations.getByRole('button', { name: 'Fermer' }).click()
    await expect(page.locator('.v-list-item').filter({ hasText: 'PEA' })).toContainText('1 250,40')

    // Objectif d'épargne rattaché au livret (J1, J2) et épargne de précaution (J3).
    await page.locator('.v-navigation-drawer').getByText('Objectifs', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Objectifs d’épargne' })).toBeVisible()
    await expect(page.getByTestId('emergency-fund')).toContainText('Livret A')
    await page.getByRole('button', { name: 'Nouvel objectif' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Nom').fill('Vacances')
    await dialog.getByLabel('Montant cible').fill('1 200')
    const target = new Date()
    target.setMonth(target.getMonth() + 5, 15)
    await dialog.getByLabel('Date cible (facultative)', { exact: true }).fill(iso(target))
    await pick(page, 'Compte', 'Livret A')
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog).toBeHidden()
    const goal = page.getByTestId('savings-goal').filter({ hasText: 'Vacances' })
    await expect(goal).toContainText('Solde de Livret A')
    await expect(goal.getByTestId('goal-advice')).toContainText('Versement conseillé')

    // Tableau de bord : patrimoine sur 24 mois (G5) et résumé des objectifs.
    await page.locator('.v-navigation-drawer').getByText('Accueil', { exact: true }).click()
    const netWorth = page.getByTestId('net-worth-chart')
    await expect(netWorth.locator('canvas')).toBeVisible()
    await netWorth.getByTestId('net-worth-length').getByRole('button', { name: '24 mois' }).click()
    await expect(netWorth).toContainText('sur 24 mois')
    await expect(page.getByTestId('goals-summary')).toContainText('Vacances')
    await expect(page.getByTestId('emergency-fund-summary')).toContainText('Épargne de précaution')
    expect(csp).toEqual([])
  })

  test('objectifs sur mobile', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'mobile', 'Parcours mobile.')
    await login(page)
    await page.goto('/objectifs')
    await expect(page.getByRole('heading', { name: 'Objectifs d’épargne' })).toBeVisible()
    await expect(page.getByTestId('savings-goal').filter({ hasText: 'Vacances' })).toContainText('Versement conseillé')
  })
})
