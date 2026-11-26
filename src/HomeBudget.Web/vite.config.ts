/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vuetify from 'vite-plugin-vuetify'
import { VitePWA } from 'vite-plugin-pwa'

const api = 'http://localhost:5080'

export default defineConfig({
  plugins: [
    vue(),
    vuetify({ autoImport: true }),
    // PWA installable (L2) et consultation hors ligne (L4).
    VitePWA({
      registerType: 'prompt',
      injectRegister: false, // enregistrement dans src/pwa.ts : aucun script inline (CSP)
      includeAssets: ['favicon.svg', 'apple-touch-icon-180x180.png', 'push-sw.js'],
      manifest: {
        id: '/',
        name: 'Home Budget',
        short_name: 'Budget',
        description: 'Budget du foyer : opérations, budgets, prévision.',
        lang: 'fr',
        start_url: '/',
        scope: '/',
        display: 'standalone',
        orientation: 'portrait',
        theme_color: '#1e5aa8',
        background_color: '#ffffff',
        icons: [
          { src: '/pwa-192x192.png', sizes: '192x192', type: 'image/png' },
          { src: '/pwa-512x512.png', sizes: '512x512', type: 'image/png' },
          { src: '/maskable-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        // Coquille de l'appli : index.html sert toutes les pages, sauf l'API et les points techniques.
        navigateFallback: '/index.html',
        navigateFallbackDenylist: [/^\/api\//, /^\/openapi\//, /^\/health\//, /^\/metrics/],
        globPatterns: ['**/*.{js,css,html,svg,png,woff2}'],
        importScripts: ['/push-sw.js'],
        cleanupOutdatedCaches: true,
        runtimeCaching: [
          {
            // Lecture hors ligne : réseau d'abord, la dernière réponse reçue sinon. Jamais gardés : fichiers
            // exportés, sauvegardes, jeton anti-CSRF. La fonction est copiée telle quelle dans sw.js : elle ne
            // doit dépendre d'aucune variable de ce fichier.
            urlPattern: ({ url, request, sameOrigin }) =>
              sameOrigin &&
              request.method === 'GET' &&
              url.pathname.startsWith('/api/') &&
              !/^\/api\/(exports|backups|auth\/csrf|docs)(\/|$)/.test(url.pathname),
            handler: 'NetworkFirst',
            options: {
              cacheName: 'hb-api',
              networkTimeoutSeconds: 6,
              expiration: { maxEntries: 400, maxAgeSeconds: 31 * 24 * 3600 },
              cacheableResponse: { statuses: [200] },
            },
          },
        ],
      },
    }),
  ],
  resolve: {
    alias: { '@': '/src' },
  },
  build: {
    // La PWA compilée est servie par l'API ASP.NET Core.
    outDir: '../HomeBudget.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': api,
      '/openapi': api,
      '/health': api,
    },
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts'],
  },
})
