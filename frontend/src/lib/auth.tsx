import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api, onUnauthorized, refreshSession, setAccessToken } from './api'
import type { LoginResponse, Session } from './types'
import { AuthContext, type AuthContextValue, type AuthState } from './useAuth'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading' })
  const queryClient = useQueryClient()

  const start = useCallback((session: Session) => {
    setAccessToken(session.accessToken)
    setState({ status: 'authenticated', session })
  }, [])

  const end = useCallback(() => {
    setAccessToken(null)
    queryClient.clear()
    setState({ status: 'anonymous' })
  }, [queryClient])

  // On load (or F5) the httpOnly refresh cookie silently restores the session.
  useEffect(() => {
    onUnauthorized(end)
    refreshSession().then((session) => (session ? start(session) : setState({ status: 'anonymous' })))
  }, [start, end])

  const value = useMemo<AuthContextValue>(
    () => ({
      state,
      login: async (email, password) => {
        const res = await api<LoginResponse>('/auth/login', { body: { email, password } })
        if (res.requiresTwoFactor) return res.challengeToken
        start(res.session!)
        return null
      },
      completeTwoFactor: async (challengeToken, code, trustDevice) => {
        const res = await api<LoginResponse>('/auth/login/2fa', { body: { challengeToken, code, trustDevice } })
        start(res.session!)
      },
      register: async (name, email, password) => {
        const res = await api<LoginResponse>('/auth/register', { body: { name, email, password } })
        start(res.session!)
      },
      logout: async () => {
        await api('/auth/logout', { method: 'POST' }).catch(() => undefined)
        end()
      },
    }),
    [state, start, end],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
