import { expect, test } from '@playwright/test'
import { login, watchCsp } from './helpers'

test.describe('Suivis du foyer', () => {
  test('aides, frais de garde et santé sur l’année', async ({ page }) => {
    const csp = watchCsp(page)
    await login(page)
    await page.goto('/famille')
    await expect(page.getByRole('heading', { name: 'Famille' })).toBeVisible()

    // Aides (P1) : les versements suivis, ou l'explication pour en suivre.
    await expect(page.getByText('Aucune aide suivie cette année.').or(page.getByText('Versements'))).toBeVisible()

    // Garde (P2) : le montant déclarable et l'export CSV.
    await page.getByRole('tab', { name: 'Garde' }).click()
    await expect(page.getByTestId('childcare-declarable')).toContainText('Montant déclarable')
    await expect(page.getByRole('link', { name: 'CSV' })).toHaveAttribute('href', /\/api\/family\/childcare\/\d{4}\/export/)
    await expect(page.getByRole('cell', { name: 'Janvier' })).toBeVisible()

    // Santé (P4) : dépenses, remboursements et organismes.
    await page.getByRole('tab', { name: 'Santé' }).click()
    await expect(page.getByTestId('health-Reste à charge')).toBeVisible()
    await expect(page.getByText('Organismes de santé')).toBeVisible()
    await expect(page).toHaveURL(/onglet=sante/)

    // L'année précédente garde l'onglet.
    await page.getByRole('button', { name: 'Année précédente' }).click()
    await expect(page).toHaveURL(/\/famille\/\d{4}\?onglet=sante/)
    await expect(page.getByTestId('health-Dépenses')).toBeVisible()
    expect(csp).toEqual([])
  })

  test('le rôle d’une catégorie se choisit selon son type', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Parcours de bureau.')
    await login(page)
    await page.goto('/categories')
    await page.getByRole('tab', { name: 'Dépenses variables' }).click()
    await page.getByRole('listitem').filter({ hasText: /^Santé$/ }).first().click()
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByTestId('category-family-role')).toContainText('Santé')
    await dialog.getByRole('button', { name: 'Annuler' }).click()
  })
})
