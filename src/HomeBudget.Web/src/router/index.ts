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
      path: '/bilan/:year(\\d{4})?',
      name: 'annual',
      component: () => import('@/views/AnnualReportView.vue'),
      meta: { title: 'Bilan annuel' },
    },
    {
      path: '/rapports',
      name: 'reports',
      component: () => import('@/views/ReportsView.vue'),
      meta: { title: 'Rapports' },
    },
    {
      path: '/calendrier/:period(\\d{4}-\\d{2})?',
      name: 'calendar',
      component: () => import('@/views/CalendarView.vue'),
      meta: { title: 'Calendrier' },
    },
    {
      path: '/repartition/:period(\\d{4}-\\d{2})?',
      name: 'split',
      component: () => import('@/views/SplitView.vue'),
      meta: { title: 'Répartition' },
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
      path: '/scenarios',
      name: 'scenarios',
      component: () => import('@/views/ScenariosView.vue'),
      meta: { title: 'Scénarios' },
    },
    {
      path: '/objectifs',
      name: 'savings',
      component: () => import('@/views/SavingsGoalsView.vue'),
      meta: { title: 'Objectifs d’épargne' },
    },
    {
      path: '/abonnements',
      name: 'subscriptions',
      component: () => import('@/views/SubscriptionsView.vue'),
      meta: { title: 'Abonnements' },
    },
    {
      path: '/rapprochement',
      name: 'reconciliation',
      component: () => import('@/views/ReconciliationView.vue'),
      meta: { title: 'Rapprochement bancaire' },
    },
    {
      path: '/avances',
      name: 'advances',
      component: () => import('@/views/AdvancesView.vue'),
      meta: { title: 'Avances à recevoir' },
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
      path: '/utilisateurs',
      name: 'users',
      component: () => import('@/views/UsersView.vue'),
      meta: { title: 'Utilisateurs', admin: true },
    },
    {
      path: '/journal',
      name: 'audit',
      component: () => import('@/views/AuditView.vue'),
      meta: { title: 'Journal d’audit', admin: true },
    },
    {
      path: '/invitation',
      name: 'invitation',
      component: () => import('@/views/InvitationView.vue'),
      meta: { public: true, title: 'Invitation' },
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
  if (auth.twoFactorPending && !to.meta.public && to.name !== 'security') return { name: 'security' }
  if (to.meta.admin && !auth.isAdmin) return { name: 'home' }
  return true
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · Home Budget` : 'Home Budget'
})
