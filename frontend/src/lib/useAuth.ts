import { createContext, useContext } from 'react'
import type { Session } from './types'

export type AuthState =
  | { status: 'loading' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; session: Session }

export type AuthContextValue = {
  state: AuthState
  /** Returns the challenge token when the account uses 2FA on an untrusted device. */
  login: (email: string, password: string) => Promise<string | null>
  completeTwoFactor: (challengeToken: string, code: string, trustDevice: boolean) => Promise<void>
  register: (name: string, email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used inside AuthProvider')
  return ctx
}
