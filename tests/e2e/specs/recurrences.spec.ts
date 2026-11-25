import { expect, test } from '@playwright/test'
import { login, watchCsp } from './helpers'

test.describe.serial('récurrences', () => {
  test('récurrence à valider, confirmation avec montant ajusté et prévision', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'La récurrence est créée une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    const drawer = page.locator('.v-navigation-drawer')
    await drawer.getByText('Récurrences', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Récurrences' })).toBeVisible()

    // Première échéance aujourd'hui, en mode « à valider » : elle apparaît tout de suite dans « À valider ».
    await page.getByRole('button', { name: 'Nouvelle récurrence' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Libellé', { exact: true }).fill('Électricité')
    await dialog.getByLabel('Montant', { exact: true }).fill('45')
    await dialog.getByRole('radio', { name: /À valider/ }).check()
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog).toBeHidden()

    const item = page.getByRole('listitem').filter({ hasText: 'Électricité' })
    await expect(item).toContainText('Tous les mois')
    await expect(item).toContainText('-45,00')
    await expect(item).toContainText('1 à valider')

    await drawer.getByText('À valider', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'À valider' })).toBeVisible()
    const card = page.locator('.v-card').filter({ hasText: 'Électricité' })
    await card.getByRole('button', { name: 'Valider' }).click()
    await card.getByLabel('Montant réel').fill('47,30')
    await card.getByRole('button', { name: 'Confirmer' }).click()
    await expect(page.getByText('Aucune échéance à valider.')).toBeVisible()

    // L'opération est créée avec le montant réel.
    await drawer.getByText('Opérations', { exact: true }).click()
    await expect(page.getByRole('listitem').filter({ hasText: 'Électricité' }).first()).toContainText('47,30')

    // Accueil : prévision de fin de période et reste à vivre.
    await drawer.getByText('Accueil', { exact: true }).click()
    const forecast = page.getByTestId('forecast')
    await expect(forecast).toContainText('Fin de période')
    await expect(forecast).toContainText('Solde prévu')
    await expect(forecast).toContainText('Reste à vivre')
    expect(csp).toEqual([])
  })
})
