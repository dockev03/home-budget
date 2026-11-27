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

  test('une déclaration Pajemploi se saisit en cliquant sur un mois', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Parcours de bureau (la déclaration ne se saisit qu’une fois).')
    const csp = watchCsp(page)
    await login(page)
    // Année sans autre donnée : le tiers « Leclerc » vient du parcours des opérations.
    await page.goto('/famille/2020?onglet=garde')
    await page.getByTestId('childcare-month-11').click()
    const dialog = page.getByTestId('childcare-declarations')
    await expect(dialog).toContainText('Aucune déclaration pour ce mois.')
    await dialog.getByTestId('declaration-add').click()

    await dialog.getByRole('combobox', { name: 'Salarié' }).fill('Lecl')
    await page.getByRole('option', { name: 'Leclerc' }).click()
    await dialog.getByTestId('declaration-activityDays').getByRole('textbox').fill('16')
    await dialog.getByTestId('declaration-normalHours').getByRole('textbox').fill('142')
    await dialog.getByTestId('declaration-netSalary').getByRole('textbox').fill('638,25')
    await dialog.getByTestId('declaration-maintenanceAllowance').getByRole('textbox').fill('67,50')
    await dialog.getByTestId('declaration-mealAllowance').getByRole('textbox').fill('82,50')
    await dialog.getByTestId('declaration-contributions').getByRole('textbox').fill('539,53')
    await dialog.getByTestId('declaration-coveredContributions').getByRole('textbox').fill('539,53')
    await dialog.getByTestId('declaration-cmgEntitlement').getByRole('textbox').fill('446,40')
    // Le récapitulatif se calcule pendant la saisie, comme sur Pajemploi.
    const totals = dialog.getByTestId('declaration-totals')
    await expect(totals).toContainText('1 327,78')
    await expect(totals).toContainText('341,85')
    await dialog.getByTestId('declaration-save').click()

    await expect(dialog.getByTestId('childcare-declaration')).toContainText('Leclerc')
    await expect(dialog.getByTestId('childcare-declaration')).toContainText('341,85')
    await dialog.getByRole('button', { name: 'Fermer' }).click()
    await expect(page.getByTestId('childcare-month-11')).toContainText('déclaré')
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
