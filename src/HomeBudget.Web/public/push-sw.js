/* Notifications push (M1), importé par le service worker généré par Workbox. */
/* global self, clients */

self.addEventListener('push', (event) => {
  let message = { title: 'Home Budget', body: '', url: '/', tag: undefined }
  try {
    message = { ...message, ...event.data.json() }
  } catch {
    // Message illisible : notification générique.
  }
  event.waitUntil(
    self.registration.showNotification(message.title, {
      body: message.body,
      tag: message.tag,
      icon: '/pwa-192x192.png',
      badge: '/badge-96x96.png',
      lang: 'fr',
      data: { url: message.url },
    }),
  )
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const target = new URL(event.notification.data?.url || '/', self.location.origin)
  // Seules les pages de l'appli s'ouvrent depuis une notification.
  const url = target.origin === self.location.origin ? target.href : self.location.origin + '/'
  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windows) => {
      const open = windows.find((w) => new URL(w.url).origin === self.location.origin)
      if (open) return open.focus().then((w) => w.navigate(url))
      return clients.openWindow(url)
    }),
  )
})
