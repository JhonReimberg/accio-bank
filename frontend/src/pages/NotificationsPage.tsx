import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Card, Empty, ErrorBox, Loading, PageHeader } from '../components/ui'
import { api } from '../lib/api'
import { dateTime } from '../lib/format'
import { keys } from '../lib/queries'
import type { Notification, Paged } from '../lib/types'

export default function NotificationsPage() {
  const client = useQueryClient()
  const list = useQuery({ queryKey: [...keys.notifications, 'list'], queryFn: () => api<Paged<Notification>>('/notifications?pageSize=50') })

  // Opening the center marks everything as read (the list still shows which ones were new).
  const hasUnread = list.data?.items.some((n) => !n.read)
  useEffect(() => {
    if (hasUnread)
      api('/notifications/read-all', { method: 'POST' }).then(() =>
        client.invalidateQueries({ queryKey: [...keys.notifications, 'unread'] }),
      )
  }, [hasUnread, client])

  return (
    <div className="stack-lg">
      <PageHeader title="Notificações" />
      <Card>
        {list.isPending ? (
          <Loading />
        ) : list.isError ? (
          <ErrorBox error={list.error} retry={list.refetch} />
        ) : list.data.items.length === 0 ? (
          <Empty>Nada por aqui ainda.</Empty>
        ) : (
          <ul className="list">
            {list.data.items.map((n) => (
              <li key={n.id} className={n.read ? '' : 'unread'}>
                <div>
                  <p>
                    <strong>{n.title}</strong>
                  </p>
                  <p>{n.body}</p>
                  <p className="muted small">{dateTime(n.createdAt)}</p>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}
