import { expect, test } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { login, watchCsp } from './helpers'

// Exécuté après les autres parcours (ordre alphabétique) : les opérations et l'échéance validée existent déjà.
test.describe.serial('tableau de bord et export', () => {
  test('tableau de bord : chiffres clés, graphiques, navigation et thème', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Parcours PC.')
    const csp = watchCsp(page)
    await login(page)

    const figures = page.getByTestId('key-figures')
    await expect(figures).toContainText('Revenus')
    await expect(figures).toContainText('Solde actuel')
    await expect(figures).toContainText('Reste à vivre')
    await expect(page.getByTestId('expenses')).toHaveText(/[1-9]/)

    // Graphiques ECharts dessinés : courbe du solde (F2), anneau (G2), barres (G3, G4).
    await expect(page.getByTestId('forecast').locator('canvas')).toBeVisible()
    await expect(page.getByTestId('expenses-chart').locator('canvas')).toBeVisible()
    await expect(page.getByTestId('budget-chart').locator('canvas')).toBeVisible()
    await expect(page.getByTestId('history-chart').locator('canvas')).toBeVisible()

    // Navigation par flèches : une période passée n'a ni courbe ni prévision.
    await page.getByRole('button', { name: 'Période précédente' }).click()
    await expect(page).toHaveURL(/\/\d{4}-\d{2}$/)
    await expect(figures).toContainText('Solde au')
    await expect(page.getByTestId('forecast')).toHaveCount(0)
    await page.getByRole('button', { name: 'Période en cours' }).click()
    await expect(page).toHaveURL(/\/$/)
    await expect(page.getByTestId('forecast')).toBeVisible()

    // Thème sombre suivi par les graphiques.
    await page.getByRole('button', { name: 'Thème' }).click()
    await page.getByText('Sombre', { exact: true }).click()
    await expect(page.locator('.v-theme--dark').first()).toBeVisible()
    await expect(page.getByTestId('history-chart').locator('canvas')).toBeVisible()
    expect(csp).toEqual([])
  })

  test('export des opérations filtrées en CSV et en Excel', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Parcours PC.')
    await login(page)
    await page.locator('.v-navigation-drawer').getByText('Opérations', { exact: true }).click()
    await page.getByRole('textbox', { name: 'Rechercher' }).fill('Leclerc')
    await expect(page).toHaveURL(/search=Leclerc/)

    await page.getByRole('button', { name: 'Exporter' }).click()
    const csvDownload = page.waitForEvent('download')
    await page.getByText('CSV (Excel en français)').click()
    const csv = await csvDownload
    expect(csv.suggestedFilename()).toMatch(/^operations-\d{8}\.csv$/)
    const bytes = readFileSync(await csv.path())
    expect([...bytes.subarray(0, 3)]).toEqual([0xef, 0xbb, 0xbf])
    const lines = bytes.toString('utf8').slice(1).split('\r\n').filter(Boolean)
    expect(lines[0]).toMatch(/^Date;Compte;Tiers;Libellé;Catégorie;/)
    // Une ligne par ligne de ventilation, et seulement les opérations filtrées.
    expect(lines.length).toBeGreaterThanOrEqual(3)
    expect(lines.slice(1).every((l) => l.includes(';Leclerc;'))).toBe(true)
    expect(lines.some((l) => l.includes(';Courses;') && l.includes(';-85,30;'))).toBe(true)
    expect(lines.some((l) => l.includes(';Chien;') && l.includes(';-24,99;'))).toBe(true)

    await page.getByRole('button', { name: 'Exporter' }).click()
    const xlsxDownload = page.waitForEvent('download')
    await page.getByText('Classeur Excel (.xlsx)').click()
    const xlsx = await xlsxDownload
    expect(xlsx.suggestedFilename()).toMatch(/^operations-\d{8}\.xlsx$/)
    expect(readFileSync(await xlsx.path()).subarray(0, 2).toString()).toBe('PK')
  })

  test('sauvegarde : téléchargement et confirmation de la restauration', async ({ page }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'Parcours PC.')
    await login(page)
    await page.locator('.v-navigation-drawer').getByText('Paramètres', { exact: true }).click()
    const backup = page.getByTestId('backup')

    const download = page.waitForEvent('download')
    await backup.getByRole('link', { name: 'Télécharger la sauvegarde' }).click()
    const file = await download
    expect(file.suggestedFilename()).toMatch(/^home-budget-sauvegarde-.*\.json$/)
    const content = JSON.parse(readFileSync(await file.path(), 'utf8'))
    expect(content.format).toBe('home-budget-backup')
    expect(content.transactions.length).toBeGreaterThan(0)

    // Le bouton ne s'active qu'avec un fichier et le mot RESTAURER (la restauration elle-même est testée par l'API).
    await backup.locator('input[type=file]').setInputFiles(await file.path())
    const restore = backup.getByRole('button', { name: 'Restaurer' })
    await expect(restore).toBeDisabled()
    await backup.getByLabel('Saisissez RESTAURER pour confirmer').fill('restaurer')
    await expect(restore).toBeDisabled()
    await backup.getByLabel('Saisissez RESTAURER pour confirmer').fill('RESTAURER')
    await expect(restore).toBeEnabled()
  })
})
