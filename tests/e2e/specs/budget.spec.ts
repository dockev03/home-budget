import { expect, test } from '@playwright/test'
import { login, watchCsp } from './helpers'

test.describe.serial('budget', () => {
  test('budget en montant et en pourcentage, récapitulatif et navigation entre périodes', async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Les budgets sont créés une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    await page.locator('.v-navigation-drawer').getByText('Budget', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Budget' })).toBeVisible()
    await expect(page.getByText('Récapitulatif')).toBeVisible()

    // Revenu en montant fixe : il sert de base aux pourcentages.
    await page.getByRole('listitem', { name: 'Budget Salaire Kévin' }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByRole('button', { name: '% des revenus' })).toHaveCount(0)
    await dialog.getByRole('button', { name: 'Montant' }).click()
    await dialog.getByLabel('Montant par période').fill('2 000')
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog).toBeHidden()

    // Courses : 15 % des revenus budgétés = 300 €.
    await page.getByRole('listitem', { name: 'Budget Courses' }).click()
    await dialog.getByRole('button', { name: '% des revenus' }).click()
    await dialog.getByLabel('Pourcentage des revenus budgétés').fill('15')
    await expect(dialog.getByText(/Soit 300,00/)).toBeVisible()
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog).toBeHidden()

    const courses = page.getByRole('listitem', { name: 'Budget Courses' })
    await expect(courses).toContainText('15 % des revenus budgétés')
    await expect(courses).toContainText('300,00')
    // Les dépenses fixes sont en automatique par défaut.
    await expect(page.getByRole('listitem', { name: 'Budget Internet' })).toContainText(
      'Automatique (par défaut)',
    )

    // Navigation : période suivante, puis retour à la période en cours.
    const current = await page.locator('.text-subtitle-1').first().textContent()
    await page.getByRole('button', { name: 'Période suivante' }).click()
    await expect(page).toHaveURL(/\/budget\/\d{4}-\d{2}$/)
    await expect(page.locator('.text-subtitle-1').first()).not.toHaveText(current!)
    await page.getByRole('button', { name: 'Période en cours' }).click()
    await expect(page).toHaveURL(/\/budget$/)
    await expect(page.locator('.text-subtitle-1').first()).toHaveText(current!)
    expect(csp).toEqual([])
  })

  test('paramètres du foyer réservés à l’administrateur', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Parcours PC.')
    await login(page)

    await page.locator('.v-navigation-drawer').getByText('Paramètres', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Paramètres du foyer' })).toBeVisible()
    await expect(page.getByLabel('Calendaire : du 1er au dernier jour du mois')).toBeChecked()
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByText('Paramètres enregistrés.')).toBeVisible()
  })

  test('budget sur mobile', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'mobile', 'Parcours mobile.')
    await login(page)

    await page.locator('.v-bottom-navigation').getByText('Budget', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Budget' })).toBeVisible()
    await expect(page.getByRole('listitem', { name: 'Budget Courses' })).toContainText('300,00')
  })
})
