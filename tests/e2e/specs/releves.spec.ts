import { expect, test } from '@playwright/test'
import { login, watchCsp } from './helpers'

/** Relevé OFX fictif (SGML, comme l'export de La Banque Postale), daté du jour. */
function statement(): Buffer {
  const today = new Date().toISOString().slice(0, 10).replaceAll('-', '')
  const line = (fitId: string, amount: string, name: string) =>
    `<STMTTRN><TRNTYPE>POS<DTPOSTED>${today}<TRNAMT>${amount}<FITID>${fitId}<NAME>${name}</STMTTRN>`
  return Buffer.from(
    'OFXHEADER:100\r\nDATA:OFXSGML\r\nVERSION:102\r\n\r\n<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><BANKTRANLIST>' +
      line('E2E-1', '-3.80', 'ACHAT CB FROMAGERIE E2E') +
      line('E2E-2', '120.00', 'VIREMENT DE REMBOURSEMENT E2E') +
      '</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>',
    'ascii',
  )
}

test.describe.serial('import de relevé', () => {
  test('aperçu, import, puis doublons reconnus au second import', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Le relevé est importé une seule fois.')
    const csp = watchCsp(page)
    await login(page)

    await page.locator('.v-navigation-drawer').getByText('Importer', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Importer un relevé' })).toBeVisible()
    await expect(page.locator('.v-select').filter({ hasText: 'Compte' }).first()).toContainText('Compte joint')

    const file = page.locator('input[type="file"]')
    await file.setInputFiles({ name: 'releve.ofx', mimeType: 'application/x-ofx', buffer: statement() })
    await page.getByRole('button', { name: 'Analyser' }).click()
    await expect(page.getByText('Nouvelle : 2')).toBeVisible()
    await expect(page.getByText('ACHAT CB FROMAGERIE E2E')).toBeVisible()

    await page.getByRole('button', { name: 'Importer 2 opération(s)' }).click()
    await expect(page.getByText('2 opération(s) importée(s)')).toBeVisible()
    await expect(page.getByText('releve.ofx → Compte joint')).toBeVisible()

    // Le même relevé : les deux opérations sont reconnues (FITID) et ne peuvent pas être importées.
    await file.setInputFiles({ name: 'releve.ofx', mimeType: 'application/x-ofx', buffer: statement() })
    await page.getByRole('button', { name: 'Analyser' }).click()
    await expect(page.getByText('Déjà importée : 2')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Importer 0 opération(s)' })).toBeDisabled()

    await page.locator('.v-navigation-drawer').getByText('Opérations', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Opérations' })).toBeVisible()
    await expect(page.locator('.v-list-item', { hasText: 'ACHAT CB FROMAGERIE E2E' })).toContainText('-3,80')
    expect(csp).toEqual([])
  })
})
