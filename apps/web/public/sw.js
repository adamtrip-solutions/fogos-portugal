// FogosPortugal Web Push service worker (v1).
//
// Hand-rolled, no Workbox. Its only jobs are: take control quickly on install,
// render an incoming push as a notification, and route a click back into the
// app. There is deliberately NO fetch handler — this SW does not cache or proxy
// anything, so it can never interfere with the SSR app's network behaviour.

const ICON = '/icon-192.png' // 192px PWA icon from public/manifest.json.
const FALLBACK_TITLE = 'FogosPortugal'

// Activate the new SW immediately rather than waiting for existing tabs to close.
self.addEventListener('install', () => {
  self.skipWaiting()
})

// Claim open clients so this SW controls every tab as soon as it activates.
self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim())
})

// Push: the backend sends JSON `{ title, body, url, tag }` (see
// Fogos.Infrastructure/Notifications/WebPushSender.cs → WebPushPayload). Parse
// defensively — a malformed/absent payload must still surface *something*.
self.addEventListener('push', (event) => {
  let payload = {}
  try {
    payload = event.data ? event.data.json() : {}
  } catch {
    payload = {}
  }

  const title = typeof payload.title === 'string' && payload.title ? payload.title : FALLBACK_TITLE
  const body = typeof payload.body === 'string' ? payload.body : ''
  const tag = typeof payload.tag === 'string' ? payload.tag : undefined
  const url = typeof payload.url === 'string' ? payload.url : '/'

  event.waitUntil(
    self.registration.showNotification(title, {
      body,
      tag,
      icon: ICON,
      badge: ICON,
      data: { url },
    }),
  )
})

// Click: close the toast, then focus an already-open same-origin tab (navigating
// it to the deep link) or open a new window when none is available.
self.addEventListener('notificationclick', (event) => {
  event.notification.close()

  const target = (event.notification.data && event.notification.data.url) || '/'
  const targetUrl = new URL(target, self.location.origin)

  event.waitUntil(
    self.clients
      .matchAll({ type: 'window', includeUncontrolled: true })
      .then((clients) => {
        for (const client of clients) {
          // Reuse any same-origin tab: navigate it to the alert's target.
          if (new URL(client.url).origin === self.location.origin && 'focus' in client) {
            return client.focus().then((c) => (c && 'navigate' in c ? c.navigate(targetUrl.href) : c))
          }
        }
        return self.clients.openWindow ? self.clients.openWindow(targetUrl.href) : undefined
      }),
  )
})
