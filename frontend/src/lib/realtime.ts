import { useEffect, useState } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { getAccessToken } from './api'
import { useMoneyMoved } from './queries'

export type LiveNotification = { id: string; title: string; body: string }

/** Listens to the notification hub while signed in; refreshes balances/statement and returns the latest toast. */
export function useRealtimeNotifications() {
  const moneyMoved = useMoneyMoved()
  const [toast, setToast] = useState<LiveNotification | null>(null)

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/api/hubs/notifications', { accessTokenFactory: () => getAccessToken() ?? '' })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()
    connection.on('notification', (n: LiveNotification) => {
      moneyMoved()
      setToast(n)
    })
    // Best effort: the notification center still works by polling if the hub is unreachable.
    connection.start().catch(() => {})
    return () => {
      connection.stop().catch(() => {})
    }
    // moneyMoved is recreated each render; the connection must live for the whole session.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    if (!toast) return
    const timer = setTimeout(() => setToast(null), 6000)
    return () => clearTimeout(timer)
  }, [toast])

  return { toast, dismiss: () => setToast(null) }
}
