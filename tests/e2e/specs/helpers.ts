import { expect, type Page } from '@playwright/test'
import { existsSync, readFileSync, writeFileSync } from 'node:fs'
import { totp } from './totp'

export const setupToken = process.env.E2E_SETUP_TOKEN ?? 'e2e-setup-token'
export const userName = 'admin'
export const password = 'une phrase de passe solide'
// La clé TOTP est partagée entre projets (desktop puis mobile), exécutés dans des workers distincts.
const stateFile = new URL('../test-results/e2e-state.json', import.meta.url)
export const saveKey = (key: string) => writeFileSync(stateFile, JSON.stringify({ key }))
export const loadKey = (): string => (existsSync(stateFile) ? JSON.parse(readFileSync(stateFile, 'utf8')).key : '')

/** Échoue si la politique CSP bloque une ressource (script, style, image…). */
export function watchCsp(page: Page): string[] {
  const violations: string[] = []
  page.on('console', (msg) => {
    if (msg.type() === 'error' && /Content Security Policy/i.test(msg.text())) violations.push(msg.text())
  })
  return violations
}

export async function login(page: Page) {
  await page.goto('/')
  await expect(page).toHaveURL(/\/connexion/)
  await page.getByLabel('Identifiant').fill(userName)
  await page.getByLabel('Mot de passe', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Se connecter' }).click()
  await expect(page.getByText('Double authentification')).toBeVisible()
  await page.getByLabel('Code', { exact: true }).fill(totp(loadKey()))
  await page.getByRole('button', { name: 'Valider' }).click()
  await expect(page.getByRole('heading', { name: `Bonjour ${userName}` })).toBeVisible()
}
