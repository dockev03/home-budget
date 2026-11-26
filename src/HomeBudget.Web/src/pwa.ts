import { ref } from 'vue'
import { registerSW } from 'virtual:pwa-register'

/** Nouvelle version de l'appli téléchargée, en attente d'activation. */
export const updateAvailable = ref(false)

let update: ((reload?: boolean) => Promise<void>) | null = null

/** Enregistre le service worker (coquille hors ligne, notifications push). */
export function registerServiceWorker() {
  if (!('serviceWorker' in navigator) || import.meta.env.DEV) return
  update = registerSW({
    onNeedRefresh() {
      updateAvailable.value = true
    },
    onRegisteredSW(_url, registration) {
      // Vérifie les mises à jour toutes les heures, l'appli restant souvent ouverte sur le téléphone.
      if (registration) setInterval(() => void registration.update(), 60 * 60 * 1000)
    },
  })
}

/** Active la nouvelle version et recharge la page. */
export async function applyUpdate() {
  updateAvailable.value = false
  await update?.(true)
}
