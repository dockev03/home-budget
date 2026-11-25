import { expect, test, type Page } from '@playwright/test'
import { login, watchCsp } from './helpers'

/** Relevé OFX fictif daté du jour : (identifiant, montant, libellé). */
function statement(...lines: [string, string, string][]): Buffer {
  const today = new Date().toISOString().slice(0, 10).replaceAll('-', '')
  const body = lines
    .map(([fitId, amount, name]) => `<STMTTRN><DTPOSTED>${today}<TRNAMT>${amount}<FITID>${fitId}<NAME>${name}</STMTTRN>`)
    .join('')
  return Buffer.from(`<OFX><BANKTRANLIST>${body}</BANKTRANLIST></OFX>`, 'ascii')
}

async function analyze(page: Page, buffer: Buffer) {
  await page.locator('.v-navigation-drawer').getByText('Importer', { exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Importer un relevé' })).toBeVisible()
  await page.locator('input[type="file"]').setInputFiles({ name: 'regles.ofx', mimeType: 'application/x-ofx', buffer })
  await page.getByRole('button', { name: 'Analyser' }).click()
}

async function openOperations(page: Page) {
  await page.locator('.v-navigation-drawer').getByText('Opérations', { exact: true }).click()
  await expect(page.getByRole('heading', { name: 'Opérations' })).toBeVisible()
}

test.describe.serial('règles apprises', () => {
  test('correction propagée, règle appliquée au relevé suivant, puis tags en masse', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Les données sont créées une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    // Premier relevé : trois passages au fournil, libellés bruts de la banque.
    await analyze(
      page,
      statement(
        ['RA-1', '-2.10', 'CB FOURNIL E2E 01'],
        ['RA-2', '-2.30', 'CB FOURNIL E2E 02'],
        ['RA-3', '-6.70', 'CB FLEURISTE E2E'],
      ),
    )
    await page.getByRole('button', { name: 'Importer 3 opération(s)' }).click()
    await expect(page.getByText('3 opération(s) importée(s)')).toBeVisible()

    // Correction d'une opération : la même correction est proposée pour l'autre passage.
    await openOperations(page)
    await page.locator('.v-list-item', { hasText: 'CB FOURNIL E2E 02' }).click()
    const dialog = page.getByRole('dialog')
    await dialog.getByRole('combobox', { name: 'Tiers' }).fill('Fournil du coin')
    await dialog.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(dialog.getByTestId('similar')).toContainText('1 autre(s) opération(s)')
    await expect(dialog.getByTestId('similar')).toContainText('CB FOURNIL E2E 01')
    await dialog.getByRole('button', { name: 'Appliquer aux 1 opération(s)' }).click()
    await expect(page.getByRole('dialog')).toBeHidden()
    await expect(page.locator('.v-list-item', { hasText: 'Fournil du coin' })).toHaveCount(2)

    // Relevé suivant : la règle apprise donne le tiers.
    await analyze(page, statement(['RA-4', '-2.50', 'CB FOURNIL E2E 03']))
    await expect(page.getByText('Règle apprise : Fournil du coin')).toBeVisible()
    await page.getByRole('button', { name: 'Importer 1 opération(s)' }).click()
    await expect(page.getByText('1 opération(s) importée(s)')).toBeVisible()

    // Modification en masse : un tag sur les trois passages, après confirmation.
    await openOperations(page)
    await expect(page.locator('.v-list-item', { hasText: 'Fournil du coin' })).toHaveCount(3)
    await page.getByRole('button', { name: 'Sélectionner', exact: true }).click()
    const boxes = page.getByRole('checkbox', { name: 'Sélectionner Fournil du coin' })
    for (let i = 0; i < 3; i++) await boxes.nth(i).click()
    await expect(page.getByText('3 sélectionnée(s)')).toBeVisible()
    await page.getByRole('button', { name: 'Modifier', exact: true }).click()
    const bulk = page.getByRole('dialog')
    await bulk.getByRole('combobox', { name: 'Ajouter des tags' }).fill('Pain')
    await bulk.getByRole('combobox', { name: 'Ajouter des tags' }).press('Enter')
    await bulk.getByRole('button', { name: 'Modifier', exact: true }).click()
    await expect(bulk.getByTestId('bulk-summary')).toContainText('tags ajoutés : Pain')
    await bulk.getByRole('button', { name: 'Confirmer' }).click()
    await expect(page.getByText('3 opérations modifiées.')).toBeVisible()
    await expect(page.locator('.v-list-item', { hasText: 'Fournil du coin' }).first()).toContainText('#Pain')
    expect(csp).toEqual([])
  })
})
