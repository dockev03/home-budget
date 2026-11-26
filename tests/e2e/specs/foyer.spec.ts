import { expect, test, type Page } from '@playwright/test'
import { login, watchCsp } from './helpers'
import { totp } from './totp'

const member = 'membre-e2e'
const memberPassword = 'phrase de passe du membre'
const privateAccount = 'Coffre E2E'

async function navigate(page: Page, label: string, heading = label) {
  await page.locator('.v-navigation-drawer').getByText(label, { exact: true }).click()
  await expect(page.getByRole('heading', { name: heading })).toBeVisible()
}

test('invitation d’un membre, double authentification imposée, compte privé invisible', async ({
  page,
  browser,
}, testInfo) => {
  test.skip(testInfo.project.name !== 'desktop', 'Le membre est invité une seule fois.')
  const csp = watchCsp(page)
  await login(page)

  // L'administrateur invite un membre et récupère le lien (affiché une seule fois).
  await navigate(page, 'Utilisateurs')
  await page.getByRole('button', { name: 'Inviter' }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Identifiant de connexion').fill(member)
  await dialog.getByLabel('Nom affiché (facultatif)').fill('Membre E2E')
  await dialog.getByRole('button', { name: 'Créer le lien' }).click()
  const link = await dialog.getByTestId('invitation-link').locator('input').inputValue()
  expect(link).toMatch(/\/invitation#[\w-]{20,}$/)
  await dialog.getByRole('button', { name: 'Fermer' }).click()
  await expect(page.getByText('Invitations en attente')).toBeVisible()

  // Le membre choisit son mot de passe, puis doit activer la double authentification.
  const context = await browser.newContext({ locale: 'fr-FR' })
  const memberPage = await context.newPage()
  const memberCsp = watchCsp(memberPage)
  await memberPage.goto(link)
  await expect(memberPage.getByText(`Votre identifiant est ${member}`)).toBeVisible()
  await memberPage.getByLabel('Mot de passe', { exact: true }).fill(memberPassword)
  await memberPage.getByLabel('Confirmation').fill(memberPassword)
  await memberPage.getByRole('button', { name: 'Créer mon compte' }).click()
  await expect(memberPage).toHaveURL(/\/securite$/)
  await expect(memberPage.getByTestId('two-factor-required')).toBeVisible()

  // Tant qu'elle n'est pas active, les autres pages renvoient vers Sécurité.
  await memberPage.locator('.v-navigation-drawer').getByText('Comptes', { exact: true }).click()
  await expect(memberPage).toHaveURL(/\/securite$/)

  await memberPage.getByRole('button', { name: 'Activer' }).click()
  const sharedKey = (await memberPage.locator('code').first().textContent()) ?? ''
  await memberPage.getByLabel('Code de vérification').fill(totp(sharedKey))
  await memberPage.getByRole('button', { name: "Confirmer l'activation" }).click()
  await expect(memberPage.getByText('Codes de secours', { exact: true })).toBeVisible()
  await memberPage.getByRole('button', { name: "J'ai noté mes codes" }).click()
  await expect(memberPage.getByTestId('two-factor-required')).toHaveCount(0)

  // Le membre crée un compte privé.
  await navigate(memberPage, 'Comptes')
  await memberPage.getByRole('button', { name: 'Nouveau compte' }).click()
  await memberPage.getByLabel('Nom').fill(privateAccount)
  await memberPage.getByLabel('Compte privé (visible de vous seul)').check()
  await memberPage.getByRole('button', { name: 'Enregistrer' }).click()
  await expect(memberPage.getByText(privateAccount)).toBeVisible()
  await expect(memberPage.getByTitle('Compte privé')).toBeVisible()
  expect(memberCsp).toEqual([])
  await context.close()

  // L'administrateur ne le voit pas, et le journal trace l'invitation acceptée.
  await navigate(page, 'Comptes')
  await expect(page.getByText(privateAccount)).toHaveCount(0)
  await navigate(page, 'Journal d’audit')
  await expect(page.getByTestId('audit-list').getByText('Invitation acceptée')).toBeVisible()
  expect(csp).toEqual([])
})
