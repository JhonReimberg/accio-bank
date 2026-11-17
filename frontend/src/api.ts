export type Account = {
  name: string
  email: string
  number: string
  availableBalance: number
  blockedBalance: number
  totalBalance: number
}

export type Tx = {
  id: string
  kind: 'Deposit' | 'InternalTransfer' | 'Pix' | 'PixRefund'
  amount: number
  description: string
  balanceAfter: number
  createdAt: string
}

const TOKEN_KEY = 'vela.token'

// shortcut: JWT in localStorage is readable by any XSS; move to an httpOnly cookie for a real bank.
export const session = {
  get: (): string | null => {
    try {
      return localStorage.getItem(TOKEN_KEY)
    } catch {
      return null
    }
  },
  set: (token: string) => {
    try {
      localStorage.setItem(TOKEN_KEY, token)
    } catch {
      /* private mode: session lasts until reload */
    }
  },
  clear: () => {
    try {
      localStorage.removeItem(TOKEN_KEY)
    } catch {
      /* nothing stored */
    }
  },
}

export class ApiError extends Error {
  status: number
  constructor(message: string, status: number) {
    super(message)
    this.status = status
  }
}

type Problem = { title?: string; errors?: Record<string, string[]> }

export async function api<T = void>(path: string, body?: unknown): Promise<T> {
  const token = session.get()
  const headers: Record<string, string> = {}
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
    // One key per user action: a network retry of the same call can never move money twice.
    headers['Idempotency-Key'] = crypto.randomUUID()
  }
  if (token) headers.Authorization = `Bearer ${token}`

  const res = await fetch(`/api${path}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers,
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) {
    const problem: Problem | null = await res.json().catch(() => null)
    const message = problem?.errors ? Object.values(problem.errors).flat().join(' ') : problem?.title
    throw new ApiError(message || 'Algo deu errado. Tente novamente.', res.status)
  }
  return res.status === 204 ? (undefined as T) : res.json()
}

export const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
export const dateTime = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
