import { expect, test, type Page } from '@playwright/test'
import { login, watchCsp } from './helpers'

/** Choisit une valeur dans un v-select du dialogue ouvert. */
async function pick(page: Page, label: string, option: string) {
  await page
    .getByRole('dialog')
    .getByRole('combobox', { name: label })
    .first()
    .locator('xpath=ancestor::div[contains(@class, "v-field ")][1]')
    .click()
  await page.getByRole('listbox', { name: label }).getByRole('option', { name: option, exact: true }).click()
}

/** Attend que le service worker contrôle la page (au premier passage, il ne la contrôle qu'après rechargement). */
async function waitForServiceWorker(page: Page) {
  await page.evaluate(() => navigator.serviceWorker.ready)
  if (!(await page.evaluate(() => !!navigator.serviceWorker.controller))) await page.reload()
  await expect.poll(() => page.evaluate(() => !!navigator.serviceWorker.controller)).toBe(true)
}

test.describe.serial('PWA, hors ligne et notifications', () => {
  test('application installable', async ({ request }) => {
    const manifest = await (await request.get('/manifest.webmanifest')).json()
    expect(manifest.name).toBe('Home Budget')
    expect(manifest.display).toBe('standalone')
    expect(manifest.icons.map((i: { sizes: string }) => i.sizes)).toEqual(expect.arrayContaining(['192x192', '512x512']))
    expect((await request.get('/sw.js')).ok()).toBe(true)
    expect((await request.get('/pwa-512x512.png')).headers()['content-type']).toBe('image/png')
  })

  test('saisie hors ligne mise en attente puis envoyée au retour du réseau', async ({ page, context }, testInfo) => {
    test.skip(testInfo.project.name !== 'desktop', 'La saisie hors ligne est faite une seule fois.')
    const csp = watchCsp(page)
    await login(page)
    await waitForServiceWorker(page)

    // Pages consultées en ligne : gardées par le service worker.
    await page.locator('.v-navigation-drawer').getByText('Budget', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Budget' })).toBeVisible()
    await page.locator('.v-navigation-drawer').getByText('Opérations', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Opérations' })).toBeVisible()

    await context.setOffline(true)
    await expect(page.getByTestId('offline-banner')).toBeVisible()

    // Consultation hors ligne : la page du budget se réaffiche depuis le cache.
    await page.locator('.v-navigation-drawer').getByText('Budget', { exact: true }).click()
    await expect(page.getByRole('heading', { name: 'Budget' })).toBeVisible()
    await expect(page.getByText('Courses').first()).toBeVisible()
    await page.locator('.v-navigation-drawer').getByText('Opérations', { exact: true }).click()

    // Saisie hors ligne : mise en file, affichée « en attente ».
    await page.getByRole('button', { name: 'Nouvelle opération' }).click()
    await pick(page, 'Compte', 'Compte joint')
    await page.getByRole('combobox', { name: 'Tiers' }).fill('Marché hors ligne')
    await page.getByLabel('Montant', { exact: true }).fill('7,40')
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByRole('dialog')).toBeHidden()
    const queue = page.getByTestId('offline-queue')
    await expect(queue).toContainText('Marché hors ligne')
    await expect(queue.getByTestId('queue-status')).toHaveText('en attente')
    await expect(page.getByTestId('offline-banner')).toContainText('(1)')

    // Retour du réseau : la file est rejouée et l'opération apparaît dans la liste.
    await context.setOffline(false)
    await expect(page.getByTestId('sync-message')).toContainText('1 opération saisie hors ligne envoyée')
    await expect(queue).toBeHidden()
    await expect(page.locator('.v-list-item', { hasText: 'Marché hors ligne' })).toContainText('-7,40')
    expect(csp).toEqual([])
  })

  test('préférences de notification', async ({ page }, testInfo) => {
    const csp = watchCsp(page)
    await login(page)
    if (testInfo.project.name === 'mobile') {
      await page.getByRole('button', { name: 'Compte' }).click()
      await page.getByRole('link', { name: 'Notifications' }).click()
    } else {
      await page.locator('.v-navigation-drawer').getByText('Notifications', { exact: true }).click()
    }
    await expect(page.getByRole('heading', { name: 'Notifications' })).toBeVisible()
    // Instance de test sans clés VAPID ni SMTP : le push et l'e-mail sont signalés comme non configurés.
    await expect(page.getByText('Le push n’est pas configuré sur le serveur')).toBeVisible()
    await expect(page.getByText('L’envoi d’e-mails n’est pas configuré sur le serveur.')).toBeVisible()

    const dueTomorrow = page.getByRole('checkbox', { name: 'Échéances de demain' })
    const before = await dueTomorrow.isChecked()
    await dueTomorrow.click()
    await page.getByRole('button', { name: 'Enregistrer' }).click()
    await expect(page.getByTestId('notification-message')).toContainText('Préférences enregistrées.')
    await page.reload()
    await expect(page.getByRole('checkbox', { name: 'Échéances de demain' })).toBeChecked({ checked: !before })
    expect(csp).toEqual([])
  })
})
