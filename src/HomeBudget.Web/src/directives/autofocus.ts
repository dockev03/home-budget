import type { Directive } from 'vue'

/**
 * Délai avant la prise du focus, comme Vuetify : laisse aux dialogues le temps de finir leur transition d'entrée
 * (contenu en visibility: hidden pendant l'animation).
 */
export const AUTOFOCUS_DELAY_MS = 50

const NON_TEXT_INPUTS = new Set([
  'button',
  'submit',
  'reset',
  'checkbox',
  'radio',
  'hidden',
  'image',
  'file',
  'range',
  'color',
])

/** Élément où l'on tape du texte : un focus déjà posé là ne doit pas être déplacé. */
function isTyping(el: Element | null): boolean {
  if (el instanceof HTMLInputElement) return !NON_TEXT_INPUTS.has(el.type)
  return (
    el instanceof HTMLTextAreaElement ||
    el instanceof HTMLSelectElement ||
    (el as HTMLElement | null)?.isContentEditable === true
  )
}

/**
 * Donne le focus au champ, sauf si l'utilisateur est déjà en train de saisir dans un autre champ. Renvoie vrai si le
 * focus a été pris.
 */
export function focusUnlessTyping(target: HTMLElement): boolean {
  const active = document.activeElement
  if (active !== target && isTyping(active)) return false
  target.focus()
  return document.activeElement === target
}

const observers = new WeakMap<HTMLElement, IntersectionObserver>()

/**
 * Remplace la prop `autofocus` de Vuetify, qui appelle `focus()` 50 ms après l'apparition du champ, sans vérifier où
 * est le focus : une saisie rapide (gestionnaire de mots de passe, test automatisé) commencée dans un autre champ
 * était alors renvoyée dans le champ « autofocus » (le mot de passe tapé dans l'identifiant). Cette directive prend
 * le focus dans les mêmes conditions, mais jamais au détriment d'un autre champ de saisie.
 *
 * Usage : `<v-text-field v-autofocus />` (ou `v-autofocus="condition"`).
 */
export const vAutofocus: Directive<HTMLElement, boolean | undefined> = {
  mounted(el, binding) {
    if (binding.value === false) return
    const target = el.matches('input,textarea') ? el : el.querySelector<HTMLElement>('input,textarea')
    if (!target) return
    const focusLater = () =>
      setTimeout(() => {
        if (target.isConnected) focusUnlessTyping(target)
      }, AUTOFOCUS_DELAY_MS)
    if (typeof IntersectionObserver === 'undefined') {
      focusLater()
      return
    }
    // Comme Vuetify : attendre que le champ soit visible (onglet, fenêtre ou dialogue affiché).
    const observer = new IntersectionObserver((entries) => {
      if (!entries.some((e) => e.isIntersecting)) return
      observer.disconnect()
      observers.delete(el)
      focusLater()
    })
    observer.observe(el)
    observers.set(el, observer)
  },
  unmounted(el) {
    observers.get(el)?.disconnect()
    observers.delete(el)
  },
}

declare module 'vue' {
  interface GlobalDirectives {
    vAutofocus: typeof vAutofocus
  }
}
