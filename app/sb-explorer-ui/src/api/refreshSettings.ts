import { useSyncExternalStore } from 'react'

let settings = { paused: false, intervalMs: null as number | null }
const listeners = new Set<() => void>()
const subscribe = (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } }
const snapshot = () => settings
export function setRefreshSettings(update: Partial<typeof settings>) {
  settings = { ...settings, ...update }
  listeners.forEach(listener => listener())
}
export function useRefreshSettings() { return useSyncExternalStore(subscribe, snapshot) }
