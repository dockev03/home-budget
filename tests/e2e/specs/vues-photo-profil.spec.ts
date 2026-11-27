import { expect, test } from '@playwright/test'
import { login, watchCsp } from './helpers'

// Petite image PNG (48 × 32, paysage) : recadrée en carré par le navigateur avant l'envoi.
const photo = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAADAAAAAgCAIAAADbtmxLAAAAkklEQVR42u3OYWbCAQCH4f9xIiIiIiJijIjIGxERY0TEiIiIiIiIGDFiRHSH91jdok+/5wRPYQnLWMEq1rCODWxiC9vYwQ/8xC72sI8DHOIIxzjBKX7hN85wjgv8wSWucI0b3OIO93jAI57wjBf8xSv+4Q3/8Y4PfFIklFBCCSWUUEIJJZRQQgkllFBCCSX0ztALVLBjiAW8RJ0AAAAASUVORK5CYII=',
  'base64',
)

test('photo de profil : choisie, affichée dans la pastille et le menu, puis retirée', async ({ page }) => {
  const csp = watchCsp(page)
  await login(page)
  const pastille = page.getByTestId('account-avatar')
  await expect(pastille.locator('img')).toHaveCount(0)

  await page.getByRole('button', { name: 'Compte' }).click()
  await page.getByTestId('avatar-menu-item').click()
  const dialog = page.getByTestId('avatar-dialog')
  await dialog.getByTestId('avatar-input').setInputFiles({ name: 'moi.png', mimeType: 'image/png', buffer: photo })
  await expect(dialog.getByRole('button', { name: 'Changer' })).toBeVisible()
  await expect(pastille.locator('img')).toHaveAttribute('src', /\/api\/account\/avatar\?v=/)
  await dialog.getByRole('button', { name: 'Fermer' }).click()

  // La photo suit l'utilisateur : rechargement de la page.
  await page.reload()
  await expect(pastille.locator('img')).toHaveAttribute('src', /\/api\/account\/avatar\?v=/)
  await page.getByRole('button', { name: 'Compte' }).click()
  await expect(page.getByTestId('account-menu-avatar').locator('img')).toBeVisible()

  await page.getByTestId('avatar-menu-item').click()
  await dialog.getByRole('button', { name: 'Retirer' }).click()
  await expect(dialog.getByRole('button', { name: 'Choisir' })).toBeVisible()
  await expect(pastille.locator('img')).toHaveCount(0)
  await expect(pastille).toHaveText('A')
  await dialog.getByRole('button', { name: 'Fermer' }).click()
  await page.getByRole('button', { name: 'Compte' }).click()
  await expect(page.getByTestId('account-menu-avatar')).toHaveText('A')
  expect(csp).toEqual([])
})
