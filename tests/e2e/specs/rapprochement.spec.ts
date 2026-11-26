import { expect, test, type Locator, type Page } from '@playwright/test'
import { login, watchCsp } from './helpers'

const account = 'Pochette E2E'

async function navigate(page: Page, label: string, heading = label) {
  await page.locator('.v-navigation-drawer').getByText(label, { exact: true }).click()
  await expect(page.getByRole('heading', { name: heading })).toBeVisible()
}

/** Choisit une valeur dans un v-select, dans le dialogue ouvert ou dans la page. */
async function pick(scope: Page | Locator, page: Page, label: string, option: string) {
  await scope
    .getByRole('combobox', { name: label })
    .first()
    .locator('xpath=ancestor::div[contains(@class, "v-field ")][1]')
    .click()
  await page.getByRole('listbox', { name: label }).getByRole('option', { name: option, exact: true }).click()
}

async function openReconciliation(page: Page) {
  await navigate(page, 'Rapprochement', 'Rapprochement bancaire')
  await pick(page, page, 'Compte', account)
}

test.describe.serial('rapprochement bancaire', () => {
  test('rapprochement, opération verrouillée, puis annulation', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Les données sont créées une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    // Un compte dédié, pour ne verrouiller aucune opération des autres parcours.
    await navigate(page, 'Comptes')
    await page.getByRole('button', { name: 'Nouveau compte' }).click()
    await page.getByLabel('Nom').fill(account)
    await page.getByLabel('Solde initial').fill('100')
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByText(account)).toBeVisible()

    await page.getByRole('button', { name: 'Nouvelle opération' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Montant', { exact: true }).fill('30')
    await pick(dialog, page, 'Compte', account)
    await dialog.getByRole('combobox', { name: 'Tiers' }).fill('Kiosque E2E')
    await dialog.getByLabel('Pointée').check()
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog).toBeHidden()

    // Relevé à 70 € : aucun écart avec le solde pointé (100 − 30).
    await openReconciliation(page)
    await page.getByLabel('Solde du relevé').fill('70')
    await page.getByRole('button', { name: 'Calculer l’écart' }).click()
    await expect(page.getByTestId('reconciliation-preview')).toContainText('Aucun écart : 1 opération(s)')
    await page.getByRole('button', { name: 'Rapprocher' }).click()
    await expect(page.getByText('1 opération(s) verrouillée(s)')).toBeVisible()

    await navigate(page, 'Opérations')
    await page.locator('.v-list-item', { hasText: 'Kiosque E2E' }).click()
    await expect(page.getByTestId('locked')).toBeVisible()
    await expect(page.getByRole('dialog').getByRole('button', { name: 'Supprimer' })).toHaveCount(0)
    await expect(page.getByRole('dialog').getByRole('button', { name: 'Enregistrer' })).toBeDisabled()
    await page.getByRole('dialog').getByRole('button', { name: 'Annuler' }).click()

    // Annulation du rapprochement : l'opération redevient modifiable.
    await openReconciliation(page)
    await page.getByRole('button', { name: 'Annuler', exact: true }).click()
    await page.getByRole('button', { name: 'Confirmer l’annulation' }).click()
    await expect(page.getByText('ses opérations sont déverrouillées')).toBeVisible()

    await navigate(page, 'Opérations')
    await page.locator('.v-list-item', { hasText: 'Kiosque E2E' }).click()
    await expect(page.getByTestId('locked')).toHaveCount(0)
    await expect(page.getByRole('dialog').getByRole('button', { name: 'Supprimer' })).toBeVisible()
    expect(csp).toEqual([])
  })
})
