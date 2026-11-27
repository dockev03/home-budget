import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, withDirectives } from 'vue'
import { AUTOFOCUS_DELAY_MS, focusUnlessTyping, vAutofocus } from './autofocus'

// Champ « autofocus » (dans un conteneur, comme v-text-field) suivi d'un second champ et d'un bouton.
const Form = defineComponent({
  props: { enabled: { type: Boolean, default: undefined } },
  setup(props) {
    return () =>
      h('form', [
        withDirectives(h('div', { class: 'field' }, [h('input', { id: 'user', type: 'text' })]), [
          [vAutofocus, props.enabled],
        ]),
        h('input', { id: 'password', type: 'password' }),
        h('button', { id: 'button', type: 'button' }, 'Ouvrir'),
      ])
  },
})

/** Monte le formulaire dans le document ; renvoie la fonction de démontage. */
function mount(enabled?: boolean): () => void {
  const host = document.createElement('div')
  document.body.appendChild(host)
  const app = createApp(Form, { enabled })
  app.mount(host)
  return () => app.unmount()
}

const byId = (id: string) => document.getElementById(id) as HTMLElement

describe('v-autofocus', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('donne le focus au champ après le délai', () => {
    mount()
    expect(document.activeElement).not.toBe(byId('user'))

    vi.advanceTimersByTime(AUTOFOCUS_DELAY_MS)

    expect(document.activeElement).toBe(byId('user'))
  })

  it('ne prend pas le focus d’un autre champ où l’on est déjà en train de saisir', () => {
    mount()
    // Saisie rapide : le mot de passe est déjà en cours de frappe quand le délai expire.
    byId('password').focus()

    vi.advanceTimersByTime(AUTOFOCUS_DELAY_MS)

    expect(document.activeElement).toBe(byId('password'))
  })

  it('prend le focus même s’il est sur un bouton (dialogue ouvert par un clic)', () => {
    mount()
    byId('button').focus()

    vi.advanceTimersByTime(AUTOFOCUS_DELAY_MS)

    expect(document.activeElement).toBe(byId('user'))
  })

  it('ne fait rien quand la condition est fausse ou après démontage', () => {
    mount(false)
    vi.advanceTimersByTime(AUTOFOCUS_DELAY_MS)
    expect(document.activeElement).toBe(document.body)

    document.body.innerHTML = ''
    const unmount = mount()
    unmount()
    vi.advanceTimersByTime(AUTOFOCUS_DELAY_MS)
    expect(document.activeElement).toBe(document.body)
  })

  it('focusUnlessTyping respecte une saisie en cours dans une zone de texte', () => {
    document.body.innerHTML = '<input id="a" /><textarea id="b"></textarea>'
    byId('b').focus()
    expect(focusUnlessTyping(byId('a'))).toBe(false)
    expect(document.activeElement).toBe(byId('b'))
  })
})
