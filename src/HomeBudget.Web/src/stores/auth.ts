import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { api, csrf, type CurrentUser, type Schemas } from '@/api/client'
import { unwrap } from '@/api/errors'

export type LoginOutcome = 'signedIn' | 'twoFactorRequired'

export const useAuthStore = defineStore('auth', () => {
  const user = ref<CurrentUser | null>(null)
  const setupRequired = ref(false)
  let initialization: Promise<void> | null = null

  const isAuthenticated = computed(() => user.value !== null)
  const isAdmin = computed(() => user.value?.roles.includes('Admin') ?? false)
  /** Saisie et modification autorisées (administrateur ou membre, pas lecture seule). */
  const canWrite = computed(() => user.value?.roles.some((r) => r === 'Admin' || r === 'Member') ?? false)

  /** Chargé une seule fois au démarrage : premier lancement ? session existante ? */
  function init(): Promise<void> {
    initialization ??= (async () => {
      const status = await unwrap(api.GET('/api/setup/status'))
      setupRequired.value = status.required
      if (!status.required) await refreshUser()
    })()
    return initialization
  }

  async function refreshUser() {
    const { data, response } = await api.GET('/api/auth/me')
    user.value = response.ok && data ? data : null
  }

  function signedIn(current: CurrentUser | null) {
    user.value = current
    csrf.reset() // le jeton anti-CSRF est lié à l'utilisateur
  }

  async function setup(request: Schemas['CreateAdminRequest']) {
    signedIn(await unwrap(api.POST('/api/setup', { body: request })))
    setupRequired.value = false
  }

  async function handleLogin(response: Schemas['LoginResponse']): Promise<LoginOutcome> {
    if (response.status === 'RequiresTwoFactor') return 'twoFactorRequired'
    signedIn(response.user)
    if (!response.user) await refreshUser()
    return 'signedIn'
  }

  async function login(userName: string, password: string, rememberMe: boolean) {
    return handleLogin(
      await unwrap(api.POST('/api/auth/login', { body: { userName, password, rememberMe } })),
    )
  }

  async function loginTwoFactor(code: string, rememberMe: boolean) {
    return handleLogin(await unwrap(api.POST('/api/auth/login/two-factor', { body: { code, rememberMe } })))
  }

  async function loginRecoveryCode(recoveryCode: string) {
    return handleLogin(await unwrap(api.POST('/api/auth/login/recovery-code', { body: { recoveryCode } })))
  }

  /** Accepte une invitation : le compte est créé et la session ouverte. */
  async function acceptInvitation(token: string, password: string) {
    signedIn(await unwrap(api.POST('/api/invitations/accept', { body: { token, password } })))
  }

  /** Double authentification imposée mais pas encore activée : seule la page Sécurité est ouverte. */
  const twoFactorPending = computed(
    () => (user.value?.twoFactorRequired ?? false) && !(user.value?.twoFactorEnabled ?? false),
  )

  async function logout() {
    try {
      await unwrap(api.POST('/api/auth/logout'))
    } finally {
      signedIn(null)
    }
  }

  return {
    user,
    setupRequired,
    isAuthenticated,
    isAdmin,
    canWrite,
    twoFactorPending,
    acceptInvitation,
    init,
    refreshUser,
    setup,
    login,
    loginTwoFactor,
    loginRecoveryCode,
    logout,
  }
})
