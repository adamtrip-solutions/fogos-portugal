// Device credentials (plan F4). Each install silently registers itself as a
// device on first API use and thereafter authenticates with
// `X-Device-Key: fdv1.{deviceId}.{deviceSecret}` — giving every phone its own
// 240 req/min App-tier bucket instead of sharing an anonymous per-IP budget
// (CGNAT'd phones would otherwise throttle each other).
//
// The secret lives in the Keychain/Keystore via expo-secure-store. Credential
// work must NEVER block or crash the UI: any failure (mutation not deployed yet,
// rate-limited, offline, secure-store error) falls back to anonymous silently.

import { Platform } from 'react-native'
import Constants from 'expo-constants'
import * as Device from 'expo-device'
import * as SecureStore from 'expo-secure-store'

import {
  createFogosClient,
  FogosApiError,
  registerAppDevice,
  type AppPlatform,
} from '@fogos/api-client'

import { FOGOS_ENDPOINT } from './config'

/** SecureStore key holding the full `fdv1.{deviceId}.{deviceSecret}` credential. */
const STORAGE_KEY = 'fogos.device-key'
const DEVICE_UNAUTHENTICATED = 'DEVICE_UNAUTHENTICATED'

// Device credentials are a native-runtime concern; SecureStore is unavailable on
// web (react-native-web), where we always stay anonymous.
const isNative = Platform.OS === 'ios' || Platform.OS === 'android'

// Anonymous client (no device header) used SOLELY to register — sending the
// device header here would recurse into registration.
const registrationClient = createFogosClient({ endpoint: FOGOS_ENDPOINT })

let cachedKey: string | null | undefined // undefined = not yet read from store
let inflight: Promise<string | null> | null = null
let reRegisterInflight: Promise<boolean> | null = null
let registrationFailed = false // in-memory only → retried on next cold start
let lastMintedAt = 0 // epoch ms of the last successful mint (0 = never)

/**
 * A credential minted within this window is treated as still-fresh by
 * reRegisterDevice: a DEVICE_UNAUTHENTICATED that arrives right after a mint
 * almost certainly raced with it, so we retry with the new key instead of
 * wiping it and minting a duplicate device.
 */
const MINT_FRESHNESS_MS = 15_000

function currentPlatform(): AppPlatform {
  return Platform.OS === 'ios' ? 'IOS' : 'ANDROID'
}

async function readStored(): Promise<string | null> {
  if (cachedKey !== undefined) return cachedKey
  let stored: string | null
  try {
    stored = (await SecureStore.getItemAsync(STORAGE_KEY)) ?? null
  } catch {
    stored = null
  }
  // A concurrent register() may have minted and cached a key while we awaited the
  // secure-store read. That in-memory value wins — unconditionally assigning the
  // (stale, likely null) read would clobber a fresh credential and trigger a
  // duplicate registration.
  if (cachedKey !== undefined) return cachedKey
  cachedKey = stored
  return cachedKey
}

/** Serializes concurrent registration attempts behind one in-flight promise. */
function register(): Promise<string | null> {
  if (inflight) return inflight
  inflight = (async () => {
    try {
      const credential = await registerAppDevice(registrationClient, {
        platform: currentPlatform(),
        model: Device.modelName,
        appVersion: Constants.expoConfig?.version ?? null,
      })
      const key = `fdv1.${credential.deviceId}.${credential.deviceSecret}`
      cachedKey = key
      lastMintedAt = Date.now()
      registrationFailed = false
      try {
        await SecureStore.setItemAsync(STORAGE_KEY, key)
      } catch {
        // Persist failed — keep it in memory for this session at least.
      }
      return key
    } catch {
      // Mutation not deployed yet (unknown-field error), rate-limited, or a
      // network failure → stay anonymous. Remember in-memory so we don't retry
      // every request, but a cold start will try again.
      registrationFailed = true
      cachedKey = null
      return null
    } finally {
      inflight = null
    }
  })()
  return inflight
}

async function ensureKey(): Promise<string | null> {
  if (!isNative) return null
  const stored = await readStored()
  if (stored) return stored
  if (registrationFailed) return null
  return register()
}

/**
 * Header provider for the Fogos client: `X-Device-Key` when a credential exists,
 * empty (anonymous) otherwise. Registers lazily on first use.
 */
export async function deviceAuthHeaders(): Promise<Record<string, string>> {
  const key = await ensureKey()
  return key ? { 'X-Device-Key': key } : {}
}

/** True when the error is a hard 401 caused by a present-but-invalid device key. */
export function isDeviceUnauthenticated(error: unknown): boolean {
  return (
    error instanceof FogosApiError &&
    error.status === 401 &&
    error.code === DEVICE_UNAUTHENTICATED
  )
}

/**
 * DEVICE_UNAUTHENTICATED recovery: wipe the stored credential and re-register
 * once. Returns true when a fresh credential was minted (caller should retry the
 * request), false to proceed anonymously.
 *
 * Serialized behind one in-flight promise (like `register`) so concurrent 401s
 * — e.g. three foreground queries failing on the same 60 s poll tick — collapse
 * into a single wipe + mint instead of each wiping a freshly minted credential
 * and spawning duplicate devices. A credential minted within the last
 * {@link MINT_FRESHNESS_MS} is assumed still-valid (this 401 raced with that
 * mint): return true immediately WITHOUT wiping so the caller retries with the
 * fresh key.
 */
export async function reRegisterDevice(): Promise<boolean> {
  if (cachedKey != null && Date.now() - lastMintedAt < MINT_FRESHNESS_MS) {
    return true
  }
  if (reRegisterInflight) return reRegisterInflight
  reRegisterInflight = (async () => {
    try {
      cachedKey = null
      registrationFailed = false
      try {
        await SecureStore.deleteItemAsync(STORAGE_KEY)
      } catch {
        // ignore — we overwrite on the next successful register anyway
      }
      return (await register()) != null
    } finally {
      reRegisterInflight = null
    }
  })()
  return reRegisterInflight
}
