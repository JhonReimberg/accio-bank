import { useSyncExternalStore } from 'react'

// Shared "hide values" toggle (the eye in the top bar). Kept in localStorage per device.
const KEY = 'vela.hideBalance'
const listeners = new Set<() => void>()

function read() {
  try {
    return localStorage.getItem(KEY) === '1'
  } catch {
    return false
  }
}

let hidden = read()

export function toggleHidden() {
  hidden = !hidden
  try {
    localStorage.setItem(KEY, hidden ? '1' : '0')
  } catch {
    /* private mode: preference lasts until reload */
  }
  listeners.forEach((l) => l())
}

export function useHidden() {
  return useSyncExternalStore(
    (l) => (listeners.add(l), () => listeners.delete(l)),
    () => hidden,
  )
}
