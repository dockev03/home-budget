import { expect, test, type Locator } from '@playwright/test'
import { login, watchCsp } from './helpers'

const top = async (locator: Locator) => (await locator.boundingBox())!.y

test.describe('Accueil personnalisable', () => {
  test('déplacer et masquer des blocs, puis rétablir la disposition', async ({ page }) => {
    const csp = watchCsp(page)
    await login(page)
    await page.goto('/')
    const keyFigures = page.getByTestId('key-figures')
    const history = page.getByTestId('history-chart')
    const accounts = page.getByTestId('accounts-card')
    await expect(keyFigures).toBeVisible()
    await expect(accounts).toBeVisible()
    expect(await top(accounts)).toBeGreaterThan(await top(history))

    // Comptes remonte au-dessus de l'historique, les chiffres clés sont masqués.
    await page.getByTestId('customize-home').click()
    const dialog = page.getByTestId('home-layout-dialog')
    for (let i = 0; i < 3; i++) await dialog.getByTestId('layout-accounts').getByTestId('layout-up').click()
    await dialog.getByTestId('layout-keyFigures').getByTestId('layout-toggle').click()
    await dialog.getByTestId('layout-save').click()
    await expect(dialog).toBeHidden()
    await expect(keyFigures).toHaveCount(0)
    expect(await top(accounts)).toBeLessThan(await top(history))

    // La disposition est enregistrée côté serveur.
    await page.reload()
    await expect(history).toBeVisible()
    await expect(keyFigures).toHaveCount(0)
    expect(await top(accounts)).toBeLessThan(await top(history))

    // Rétablir la disposition par défaut.
    await page.getByTestId('customize-home').click()
    await dialog.getByTestId('layout-reset').click()
    await expect(dialog).toBeHidden()
    await expect(keyFigures).toBeVisible()
    expect(await top(accounts)).toBeGreaterThan(await top(history))
    expect(csp).toEqual([])
  })
})
