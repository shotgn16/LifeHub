// Talks to the LifeHub desktop app window (Photino) when LifeHub is shown inside it.
export function inApp() { return !!(window.external && typeof window.external.sendMessage === 'function'); }
export function send(message) {
  if (!inApp()) return false;
  window.external.sendMessage(message);
  return true;
}
