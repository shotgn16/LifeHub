// LifeHub service worker: shows LifeHub's own push notifications and opens the right page when tapped.
// (No offline caching — LifeHub is a live app.)
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', e => e.waitUntil(self.clients.claim()));

self.addEventListener('push', e => {
  let d = {};
  try { d = e.data ? e.data.json() : {}; } catch { d = { title: 'LifeHub', body: e.data ? e.data.text() : '' }; }
  const scope = self.registration.scope;
  const url = new URL(d.url || '', scope).href;
  e.waitUntil(self.registration.showNotification(d.title || 'LifeHub', {
    body: d.body || '',
    tag: d.tag ? 'lifehub-' + d.tag : undefined,
    renotify: !!d.tag,
    icon: scope + 'icon-192.png',
    badge: scope + 'badge-96.png',
    data: { url }
  }));
});

self.addEventListener('notificationclick', e => {
  e.notification.close();
  const url = (e.notification.data && e.notification.data.url) || self.registration.scope;
  e.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(list => {
    for (const c of list) {
      if (c.url.startsWith(self.registration.scope) && 'focus' in c) {
        if ('navigate' in c) c.navigate(url).catch(() => {});
        return c.focus();
      }
    }
    return self.clients.openWindow(url);
  }));
});
