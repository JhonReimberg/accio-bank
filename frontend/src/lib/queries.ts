import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import type { Account, OnboardingStatus } from './types'

export const keys = {
  account: ['account'] as const,
  onboarding: ['onboarding'] as const,
  statement: ['statement'] as const,
  notifications: ['notifications'] as const,
  pix: ['pix'] as const,
  security: ['security'] as const,
}

export const useAccount = () => useQuery({ queryKey: keys.account, queryFn: () => api<Account>('/account') })

export const useOnboarding = () => useQuery({ queryKey: keys.onboarding, queryFn: () => api<OnboardingStatus>('/onboarding') })

export const useUnreadCount = () =>
  useQuery({
    queryKey: [...keys.notifications, 'unread'],
    queryFn: () => api<{ count: number }>('/notifications/unread-count'),
    refetchInterval: 30_000,
  })

/** After money moves: balances, statement, limits and notifications may all have changed. */
export function useMoneyMoved() {
  const client = useQueryClient()
  return () => {
    for (const key of [keys.account, keys.statement, keys.pix, keys.notifications]) client.invalidateQueries({ queryKey: key })
  }
}
