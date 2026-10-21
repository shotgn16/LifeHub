// Turns LifeHub's own push notifications on/off for this browser. Used by Settings → Notifications.
function b64ToBytes(s) {
  const pad = '='.repeat((4 - s.length % 4) % 4);
  const raw = atob((s + pad).replace(/-/g, '+').replace(/_/g, '/'));
  return Uint8Array.from(raw, c => c.charCodeAt(0));
}

function swUrl() { return new URL('sw.js', document.baseURI).href; }

export function support() {
  const standalone = window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;
  const ios = /iphone|ipad|ipod/i.test(navigator.userAgent);
  return {
    supported: 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window,
    secure: window.isSecureContext,
    permission: 'Notification' in window ? Notification.permission : 'unsupported',
    ios, standalone,
    userAgent: navigator.userAgent
  };
}

async function registration() {
  const scope = new URL('./', document.baseURI).href;
  return (await navigator.serviceWorker.getRegistration(scope)) || navigator.serviceWorker.register(swUrl(), { scope });
}

export async function current() {
  if (!('serviceWorker' in navigator)) return null;
  const reg = await navigator.serviceWorker.getRegistration(new URL('./', document.baseURI).href);
  const sub = reg ? await reg.pushManager.getSubscription() : null;
  return sub ? sub.toJSON() : null;
}

export async function subscribe(publicKey) {
  const permission = await Notification.requestPermission();
  if (permission !== 'granted') throw new Error('Notifications were not allowed in this browser.');
  const reg = await registration();
  await navigator.serviceWorker.ready;
  let sub = await reg.pushManager.getSubscription();
  if (sub) {
    // Keys changed (e.g. restored on another server)? Start over.
    const key = new Uint8Array(sub.options.applicationServerKey || []);
    const want = b64ToBytes(publicKey);
    if (key.length && (key.length !== want.length || key.some((b, i) => b !== want[i]))) { await sub.unsubscribe(); sub = null; }
  }
  sub = sub || await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: b64ToBytes(publicKey) });
  return sub.toJSON();
}

export async function unsubscribe() {
  const reg = await navigator.serviceWorker.getRegistration(new URL('./', document.baseURI).href);
  const sub = reg ? await reg.pushManager.getSubscription() : null;
  if (sub) await sub.unsubscribe();
  return true;
}
