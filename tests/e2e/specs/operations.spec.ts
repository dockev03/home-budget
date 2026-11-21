import { expect, test, type Page } from '@playwright/test'
import { login, watchCsp } from './helpers'

/** Ouvre une entrée de la navigation (menu latéral sur PC, menu du bas sur mobile). */
async function navigate(page: Page, isMobile: boolean, label: string) {
  const nav = page.locator(isMobile ? '.v-bottom-navigation' : '.v-navigation-drawer')
  await nav.getByText(label, { exact: true }).click()
  await expect(page.getByRole('heading', { name: label })).toBeVisible()
}

/** Choisit une valeur dans un v-select / v-autocomplete du dialogue ouvert, à partir de son libellé. */
async function pick(page: Page, label: string, option: string) {
  // On clique sur le champ visible (.v-field) qui contient l'élément « combobox ».
  await page
    .getByRole('dialog')
    .getByRole('combobox', { name: label })
    .first()
    .locator('xpath=ancestor::div[contains(@class, "v-field ")][1]')
    .click()
  // Options de la liste de ce champ : un menu précédent peut encore être en train de se fermer.
  await page.getByRole('listbox', { name: label }).getByRole('option', { name: option, exact: true }).click()
}

/** Choisit une valeur dans un v-autocomplete du dialogue ouvert, en tapant le début du texte (liste virtualisée). */
async function search(page: Page, label: string, option: string) {
  const field = page.getByRole('dialog').getByRole('combobox', { name: label })
  await field.locator('xpath=ancestor::div[contains(@class, "v-field ")][1]').click()
  await field.pressSequentially(option)
  // Le texte filtré est surligné (<mark>) : on cherche l'option par son contenu, pas par son nom accessible.
  await page
    .getByRole('listbox', { name: label })
    .getByRole('option')
    .filter({ hasText: new RegExp(`^${option}`) })
    .first()
    .click()
}

test.describe.serial('comptes et opérations', () => {
  test('création des comptes, dépense ventilée, virement et pointage', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Les données sont créées une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    // Comptes
    await navigate(page, false, 'Comptes')
    await page.getByRole('button', { name: 'Nouveau compte' }).click()
    await page.getByLabel('Nom').fill('Compte joint')
    await page.getByLabel('Solde initial').fill('1 500,00')
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByText('Compte joint')).toBeVisible()

    await page.getByRole('button', { name: 'Nouveau compte' }).click()
    await page.getByLabel('Nom').fill('Livret A')
    await pick(page, 'Type', 'Épargne')
    await page.getByLabel('Solde initial').fill('0')
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByTestId('net-worth')).toContainText('1 500,00')

    // Dépense ventilée sur deux catégories
    await navigate(page, false, 'Opérations')
    await page.getByRole('button', { name: 'Nouvelle opération' }).click()
    await pick(page, 'Compte', 'Compte joint')
    await page.getByRole('combobox', { name: 'Tiers' }).fill('Leclerc')
    await page.getByRole('button', { name: 'Ventiler sur plusieurs catégories' }).click()
    await search(page, 'Catégorie 1', 'Courses')
    await page.getByLabel('Montant', { exact: true }).nth(0).fill('85,30')
    await search(page, 'Catégorie 2', 'Chien')
    await page.getByLabel('Montant', { exact: true }).nth(1).fill('24,99')
    await expect(page.getByLabel('Montant total')).toHaveValue(/110,29/)
    await page.getByRole('button', { name: 'Enregistrer' }).click()

    const expense = page.locator('.v-list-item', { hasText: 'Leclerc' })
    await expect(expense).toContainText('Ventilée (2)')
    await expect(expense).toContainText('-110,29')

    // Virement vers le livret
    await page.getByRole('button', { name: 'Nouvelle opération' }).click()
    await page.getByRole('button', { name: 'Virement' }).click()
    await page.getByLabel('Montant', { exact: true }).fill('200')
    await pick(page, 'Depuis le compte', 'Compte joint')
    await pick(page, 'Vers le compte', 'Livret A')
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByText('Virement vers Livret A')).toBeVisible()
    await expect(page.getByText('Virement depuis Compte joint')).toBeVisible()

    // Pointage, puis filtre « non pointées »
    await expense.getByRole('button', { name: 'Pointer' }).click()
    await expect(expense.getByRole('button', { name: 'Dépointer' })).toBeVisible()

    // Duplication depuis le formulaire, puis suppression de la copie
    await expense.click()
    await page.getByRole('button', { name: 'Dupliquer' }).click()
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.locator('.v-list-item', { hasText: 'Leclerc' })).toHaveCount(2)
    await page.locator('.v-list-item', { hasText: 'Leclerc' }).first().click()
    await page.getByRole('button', { name: 'Supprimer' }).click()
    await page.getByRole('button', { name: 'Confirmer la suppression' }).click()
    await expect(page.locator('.v-list-item', { hasText: 'Leclerc' })).toHaveCount(1)

    // Soldes : 1 500 − 110,29 − 200 = 1 189,71 ; patrimoine inchangé par le virement.
    await navigate(page, false, 'Comptes')
    await expect(page.locator('.v-list-item', { hasText: 'Compte joint' })).toContainText('1 189,71')
    await expect(page.locator('.v-list-item', { hasText: 'Livret A' })).toContainText('200,00')
    await expect(page.getByTestId('net-worth')).toContainText('1 389,71')
    expect(csp).toEqual([])
  })

  test('saisie rapide depuis le mobile', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'mobile', 'Parcours mobile.')
    const csp = watchCsp(page)
    await login(page)

    await page.getByRole('button', { name: 'Nouvelle opération' }).click()
    await page.getByLabel('Montant', { exact: true }).fill('4,20')
    await page.getByRole('combobox', { name: 'Tiers' }).fill('Boulangerie')
    await search(page, 'Catégorie', 'Divers')
    await page.getByRole('button', { name: 'Enregistrer' }).click()

    await navigate(page, true, 'Opérations')
    await expect(page.locator('.v-list-item', { hasText: 'Boulangerie' })).toContainText('-4,20')
    expect(csp).toEqual([])
  })
})
