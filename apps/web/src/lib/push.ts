// Browser-side Web Push plumbing for /alertas. Registers the service worker,
// negotiates the Notification permission, subscribes via PushManager, and mirrors
// the resulting device to the API. Everything here is guarded so the module is
// safe to import during SSR — the page imports it at module scope.

import {
  deleteWebPushDevice,
  registerWebPushDevice,
} from '#/lib/fogos/api.ts'

const STORAGE_KEY = 'fogos:webpush-device'
const SW_URL = '/sw.js'

/** The device handle we persist locally to pair this browser with its API device. */
export interface StoredDevice {
  deviceId: string
  endpoint: string
}

/** Raised when the user has blocked notifications so the UI can explain how to unblock. */
export class PushPermissionDeniedError extends Error {
  constructor() {
    super('As notificações estão bloqueadas nas definições do navegador.')
    this.name = 'PushPermissionDeniedError'
  }
}

/**
 * Whether this browser can do Web Push at all. SSR-safe: every access is guarded
 * so the whole expression is simply `false` on the server.
 */
export function supportsPush(): boolean {
  return (
    typeof navigator !== 'undefined' &&
    'serviceWorker' in navigator &&
    typeof window !== 'undefined' &&
    'PushManager' in window &&
    'Notification' in window
  )
}

/**
 * iOS only exposes Web Push to an *installed* PWA (added to the Home Screen).
 * True when this is iOS/iPadOS in a normal browser tab, so the UI can nudge the
 * user to install first.
 */
export function iosNeedsInstall(): boolean {
  if (typeof navigator === 'undefined' || typeof window === 'undefined') return false
  const ua = navigator.userAgent
  const isIos =
    /iP(hone|ad|od)/.test(ua) ||
    // iPadOS 13+ reports as a Mac; disambiguate via touch support.
    (/Macintosh/.test(ua) && 'ontouchend' in window)
  if (!isIos) return false
  const standalone =
    window.matchMedia?.('(display-mode: standalone)').matches ||
    // Legacy iOS Safari flag.
    (navigator as unknown as { standalone?: boolean }).standalone === true
  return !standalone
}

// VAPID keys travel as base64url strings; PushManager needs the raw bytes. The
// explicit ArrayBuffer keeps the result a non-shared BufferSource (what
// `applicationServerKey` requires).
function urlBase64ToUint8Array(base64: string): Uint8Array<ArrayBuffer> {
  const padding = '='.repeat((4 - (base64.length % 4)) % 4)
  const normalized = (base64 + padding).replace(/-/g, '+').replace(/_/g, '/')
  const raw = atob(normalized)
  const output = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i++) output[i] = raw.charCodeAt(i)
  return output
}

// ── Local storage ─────────────────────────────────────────────────────────────

function readStored(): StoredDevice | null {
  if (typeof localStorage === 'undefined') return null
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<StoredDevice>
    if (typeof parsed.deviceId === 'string' && typeof parsed.endpoint === 'string') {
      return { deviceId: parsed.deviceId, endpoint: parsed.endpoint }
    }
  } catch {
    // Corrupt entry — fall through and clear.
  }
  clearStored()
  return null
}

function writeStored(device: StoredDevice): void {
  if (typeof localStorage === 'undefined') return
  localStorage.setItem(STORAGE_KEY, JSON.stringify(device))
}

function clearStored(): void {
  if (typeof localStorage === 'undefined') return
  localStorage.removeItem(STORAGE_KEY)
}

// ── Public API ────────────────────────────────────────────────────────────────

async function ensureRegistration(): Promise<ServiceWorkerRegistration> {
  const existing = await navigator.serviceWorker.getRegistration(SW_URL)
  return existing ?? navigator.serviceWorker.register(SW_URL)
}

/**
 * Returns the locally stored device, but only if the browser STILL holds a live
 * push subscription for it. If the user cleared site data (subscription gone but
 * our storage lingers), this is the repair path: clear storage and return null so
 * the UI falls back to the not-enabled state.
 */
export async function getStoredDevice(): Promise<StoredDevice | null> {
  const stored = readStored()
  if (!stored) return null
  if (!supportsPush()) return stored // can't verify; trust storage.
  try {
    const registration = await navigator.serviceWorker.getRegistration(SW_URL)
    const subscription = await registration?.pushManager.getSubscription()
    if (!subscription) {
      clearStored()
      return null
    }
    return stored
  } catch {
    return stored
  }
}

/**
 * Turns notifications on for this browser: registers the SW, asks permission,
 * subscribes to push, mirrors the device to the API, and persists the handle.
 * Throws {@link PushPermissionDeniedError} when the user blocks the prompt.
 */
export async function enablePush(publicKey: string): Promise<StoredDevice> {
  const registration = await ensureRegistration()
  await navigator.serviceWorker.ready

  const permission = await Notification.requestPermission()
  if (permission === 'denied') throw new PushPermissionDeniedError()
  if (permission !== 'granted') {
    // 'default' — the user dismissed the prompt without deciding.
    throw new Error('Permissão de notificações não concedida.')
  }

  // Reuse an existing subscription if the browser already has one, else create it.
  const subscription =
    (await registration.pushManager.getSubscription()) ??
    (await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(publicKey),
    }))

  const json = subscription.toJSON()
  const endpoint = json.endpoint
  const p256dh = json.keys?.p256dh
  const auth = json.keys?.auth
  if (!endpoint || !p256dh || !auth) {
    throw new Error('A subscrição de push do navegador está incompleta.')
  }

  const { id: deviceId } = await registerWebPushDevice({
    data: { endpoint, p256dh, auth },
  })

  const device: StoredDevice = { deviceId, endpoint }
  writeStored(device)
  return device
}

/**
 * Turns notifications off: unsubscribes locally, deletes the device on the API
 * (which cascades its anonymous subscriptions), and clears local storage. Best
 * effort — storage is always cleared even if a network step fails, so the UI can
 * never get wedged in the enabled state.
 */
export async function disablePush(): Promise<void> {
  let endpoint: string | null = readStored()?.endpoint ?? null
  try {
    const registration = await navigator.serviceWorker.getRegistration(SW_URL)
    const subscription = await registration?.pushManager.getSubscription()
    if (subscription) {
      endpoint = subscription.endpoint
      await subscription.unsubscribe()
    }
  } catch {
    // Ignore — we still attempt the API delete + storage clear below.
  }

  try {
    if (endpoint) await deleteWebPushDevice({ data: endpoint })
  } finally {
    clearStored()
  }
}
