import type { Session } from './types'

// The access token lives only in memory (never in localStorage, out of reach of injected scripts).
// The refresh token is an httpOnly cookie the browser sends to /api/auth/refresh by itself.
let accessToken: string | null = null
let onSessionLost: () => void = () => {}

export function setAccessToken(token: string | null) {
  accessToken = token
}

/** For the real-time hub (WebSockets send it in the query string). */
export const getAccessToken = () => accessToken

export function onUnauthorized(handler: () => void) {
  onSessionLost = handler
}

// Old prefix kept on purpose: renaming would make every browser look like a new device.
const DEVICE_KEY = 'vela.device'
let memoryDeviceId: string | null = null

/** Random id per browser, used by the API for trusted devices and new-device alerts. */
function deviceId(): string {
  try {
    let id = localStorage.getItem(DEVICE_KEY)
    if (!id) {
      id = crypto.randomUUID()
      localStorage.setItem(DEVICE_KEY, id)
    }
    return id
  } catch {
    memoryDeviceId ??= crypto.randomUUID()
    return memoryDeviceId
  }
}

export class ApiError extends Error {
  status: number
  code: string | null
  constructor(message: string, status: number, code: string | null) {
    super(message)
    this.status = status
    this.code = code
  }
}

type Problem = { title?: string; code?: string; errors?: Record<string, string[]> }

async function toError(res: Response): Promise<ApiError> {
  const problem: Problem | null = await res.json().catch(() => null)
  const message = problem?.errors ? Object.values(problem.errors).flat().join(' ') : problem?.title
  return new ApiError(message || 'Algo deu errado. Tente novamente.', res.status, problem?.code ?? null)
}

let refreshing: Promise<Session | null> | null = null

/** Exchanges the refresh cookie for a new access token. Concurrent callers share one request. */
export function refreshSession(): Promise<Session | null> {
  refreshing ??= fetch('/api/auth/refresh', { method: 'POST', headers: { 'X-Device-Id': deviceId() } })
    .then(async (res) => {
      if (!res.ok) return null
      const session: Session = await res.json()
      setAccessToken(session.accessToken)
      return session
    })
    .catch(() => null)
    .finally(() => {
      refreshing = null
    })
  return refreshing
}

type Options = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  body?: unknown
  form?: FormData
  /** Money movements: one Idempotency-Key per user action, reused if the call is retried. */
  idempotent?: boolean
  blob?: boolean
}

export async function api<T = void>(path: string, options: Options = {}): Promise<T> {
  const idempotencyKey = options.idempotent ? crypto.randomUUID() : null
  const send = () => {
    const headers: Record<string, string> = { 'X-Device-Id': deviceId() }
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`
    if (options.body !== undefined) headers['Content-Type'] = 'application/json'
    if (idempotencyKey) headers['Idempotency-Key'] = idempotencyKey
    return fetch(`/api${path}`, {
      method: options.method ?? (options.body !== undefined || options.form ? 'POST' : 'GET'),
      headers,
      body: options.form ?? (options.body === undefined ? undefined : JSON.stringify(options.body)),
    })
  }

  let res = await send()
  // Access tokens are short-lived: refresh once and replay the same request (same idempotency key).
  if (res.status === 401 && accessToken && !path.startsWith('/auth/')) {
    if (await refreshSession()) res = await send()
    else onSessionLost()
  }
  if (!res.ok) throw await toError(res)
  if (res.status === 204) return undefined as T
  return (options.blob ? res.blob() : res.json()) as Promise<T>
}

/** Downloads an authenticated file (e.g. a PDF receipt) and opens the browser's save dialog. */
export async function download(path: string, fileName: string) {
  const blob = await api<Blob>(path, { blob: true })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
