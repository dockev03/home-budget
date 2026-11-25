import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

declare module 'vue-router' {
  interface RouteMeta {
    /** Page accessible sans session (premier lancement, connexion). */
    public?: boolean
    /** Page réservée aux administrateurs. */
    admin?: boolean
    title?: string
  }
}

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/:period(\\d{4}-\\d{2})?',
      name: 'home',
      component: () => import('@/views/HomeView.vue'),
      meta: { title: 'Tableau de bord' },
    },
    {
      path: '/operations',
      name: 'transactions',
      component: () => import('@/views/TransactionsView.vue'),
      meta: { title: 'Opérations' },
    },
    {
      path: '/budget/:period(\\d{4}-\\d{2})?',
      name: 'budget',
      component: () => import('@/views/BudgetView.vue'),
      meta: { title: 'Budget' },
    },
    {
      path: '/import',
      name: 'import',
      component: () => import('@/views/ImportView.vue'),
      meta: { title: 'Importer un relevé' },
    },
    {
      path: '/recurrences',
      name: 'recurrences',
      component: () => import('@/views/RecurrencesView.vue'),
      meta: { title: 'Récurrences' },
    },
    {
      path: '/a-valider',
      name: 'pending',
      component: () => import('@/views/PendingView.vue'),
      meta: { title: 'À valider' },
    },
    {
      path: '/comptes',
      name: 'accounts',
      component: () => import('@/views/AccountsView.vue'),
      meta: { title: 'Comptes' },
    },
    {
      path: '/categories',
      name: 'categories',
      component: () => import('@/views/CategoriesView.vue'),
      meta: { title: 'Catégories' },
    },
    {
      path: '/securite',
      name: 'security',
      component: () => import('@/views/SecurityView.vue'),
      meta: { title: 'Sécurité' },
    },
    {
      path: '/parametres',
      name: 'settings',
      component: () => import('@/views/SettingsView.vue'),
      meta: { title: 'Paramètres', admin: true },
    },
    {
      path: '/premier-lancement',
      name: 'setup',
      component: () => import('@/views/SetupView.vue'),
      meta: { public: true, title: 'Premier lancement' },
    },
    {
      path: '/connexion',
      name: 'login',
      component: () => import('@/views/LoginView.vue'),
      meta: { public: true, title: 'Connexion' },
    },
    { path: '/:pathMatch(.*)*', name: 'not-found', component: () => import('@/views/NotFoundView.vue') },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  await auth.init()

  if (auth.setupRequired) return to.name === 'setup' ? true : { name: 'setup' }
  if (to.name === 'setup') return { name: 'login' }
  if (to.name === 'login' && auth.isAuthenticated) return { name: 'home' }
  if (!to.meta.public && !auth.isAuthenticated) {
    return { name: 'login', query: to.fullPath !== '/' ? { redirect: to.fullPath } : {} }
  }
  if (to.meta.admin && !auth.isAdmin) return { name: 'home' }
  return true
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · Home Budget` : 'Home Budget'
})
