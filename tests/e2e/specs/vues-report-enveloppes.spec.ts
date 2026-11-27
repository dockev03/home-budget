import { expect, test } from '@playwright/test'
import { login, watchCsp } from './helpers'

test.describe.serial('Report du reste et mode enveloppes', () => {
  test('report activé sur une dépense, enveloppes et déplacement de budget', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Données créées une seule fois.')
    const csp = watchCsp(page)
    await login(page)
    const drawer = page.locator('.v-navigation-drawer')

    // Report du reste (D6) : activé depuis la fenêtre du budget, sans « Enregistrer ».
    await drawer.getByText('Budget', { exact: true }).click()
    await page.getByRole('listitem', { name: 'Budget Courses' }).click()
    const dialog = page.getByRole('dialog')
    const carryOver = dialog.getByTestId('carry-over')
    // L'interrupteur suit la réponse de l'API : on clique, puis on attend l'état.
    const toggle = carryOver.getByLabel('Reporter le reste sur la période suivante')
    await toggle.click()
    await expect(toggle).toBeChecked()
    await expect(carryOver).toContainText('Depuis')
    await dialog.getByRole('button', { name: 'Annuler' }).click()

    // Mode enveloppes (D7) : activé dans les paramètres du foyer.
    await drawer.getByText('Paramètres', { exact: true }).click()
    await page.getByTestId('envelope-mode').getByLabel('Affecter chaque euro reçu à une catégorie').check()
    await page.getByRole('button', { name: 'Enregistrer' }).first().click()
    await expect(page.getByText('Paramètres enregistrés.')).toBeVisible()
    await expect(page.getByTestId('envelope-mode')).toContainText('Actif depuis')

    await drawer.getByText('Budget', { exact: true }).click()
    await expect(page.getByTestId('envelope')).toContainText(/à affecter|affectés en trop/)
    await page.getByRole('listitem', { name: 'Budget Courses' }).click()
    await expect(dialog.getByTestId('carry-over')).toContainText('Toujours actif en mode enveloppes')
    await dialog.getByRole('button', { name: 'Annuler' }).click()

    // Déplacer 10 € de Courses vers Essence, pour la période affichée seulement.
    await page.getByRole('button', { name: 'Déplacer' }).click()
    const move = page.getByRole('dialog')
    await move.getByTestId('move-from').click()
    await page.getByRole('listbox', { name: 'Depuis' }).getByRole('option', { name: /^Courses/ }).click()
    await move.getByTestId('move-to').click()
    await page.getByRole('listbox', { name: 'Vers' }).getByRole('option', { name: /^Essence/ }).click()
    await move.getByLabel('Montant').fill('10')
    await move.getByRole('button', { name: 'Déplacer' }).click()
    await expect(move).toBeHidden()
    await expect(page.getByRole('listitem', { name: 'Budget Essence' })).toContainText('cette période')
    await expect(page.getByRole('listitem', { name: 'Budget Courses' })).toContainText('cette période')

    // Retour à l'état de départ, que les tests suivants (mobile compris) attendent : budgets par défaut,
    // report désactivé, mode enveloppes désactivé.
    for (const name of ['Essence', 'Courses']) {
      await page.getByRole('listitem', { name: `Budget ${name}` }).click()
      await dialog.getByRole('button', { name: 'Revenir au budget par défaut' }).click()
      await expect(dialog).toBeHidden()
      await expect(page.getByRole('listitem', { name: `Budget ${name}` })).not.toContainText('cette période')
    }
    await expect(page.getByRole('listitem', { name: 'Budget Courses' })).toContainText('300,00')

    await drawer.getByText('Paramètres', { exact: true }).click()
    await page.getByTestId('envelope-mode').getByLabel('Affecter chaque euro reçu à une catégorie').uncheck()
    await page.getByRole('button', { name: 'Enregistrer' }).first().click()
    await expect(page.getByText('Paramètres enregistrés.')).toBeVisible()

    await drawer.getByText('Budget', { exact: true }).click()
    await expect(page.getByTestId('envelope')).toBeHidden()
    await page.getByRole('listitem', { name: 'Budget Courses' }).click()
    await toggle.click()
    await expect(toggle).not.toBeChecked()
    await dialog.getByRole('button', { name: 'Annuler' }).click()
    expect(csp).toEqual([])
  })
})
