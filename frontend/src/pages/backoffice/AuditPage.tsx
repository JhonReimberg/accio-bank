import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Button, Card, Empty, ErrorBox, Field, Loading, PageHeader, Pagination } from '../../components/ui'
import { api } from '../../lib/api'
import { dateTime } from '../../lib/format'
import type { AuditLog, Paged } from '../../lib/types'

export default function AuditPage() {
  const [filters, setFilters] = useState(new URLSearchParams())
  const [page, setPage] = useState(1)
  const query = new URLSearchParams(filters)
  query.set('page', String(page))
  query.set('pageSize', '25')
  const logs = useQuery({
    queryKey: ['backoffice', 'audit', query.toString()],
    queryFn: () => api<Paged<AuditLog>>(`/admin/audit-logs?${query}`),
    placeholderData: keepPreviousData,
  })
  const [open, setOpen] = useState<string | null>(null)

  return (
    <div className="stack-lg">
      <PageHeader title="Log de auditoria" />
      <Card>
        <form
          className="filters"
          onSubmit={(e) => {
            e.preventDefault()
            const next = new URLSearchParams()
            for (const [k, v] of new FormData(e.currentTarget)) if (v) next.set(k, String(v))
            setPage(1)
            setFilters(next)
          }}
        >
          <Field label="Ação (prefixo)" name="action" placeholder="pix., credit., auth.login" />
          <Field label="Entidade (id)" name="entityId" />
          <Field label="Usuário (id)" name="actorUserId" />
          <Field label="De" name="from" type="date" />
          <Field label="Até" name="to" type="date" />
          <Button variant="ghost">Filtrar</Button>
        </form>
      </Card>
      <Card>
        {logs.isPending ? (
          <Loading />
        ) : logs.isError ? (
          <ErrorBox error={logs.error} retry={logs.refetch} />
        ) : logs.data.items.length === 0 ? (
          <Empty>Nenhum registro.</Empty>
        ) : (
          <>
            <ul className="list list-clickable">
              {logs.data.items.map((l) => (
                <li key={l.id}>
                  <button onClick={() => setOpen(open === l.id ? null : l.id)} aria-expanded={open === l.id}>
                    <div className="stack-sm">
                      <p>
                        <span className="mono">{l.action}</span> <span className="muted small">{l.entityType}</span>
                      </p>
                      <p className="muted small">
                        {dateTime(l.createdAt)} · {l.actorUserId ? `usuário ${l.actorUserId.slice(0, 8)}…` : 'sistema'}
                        {l.ipAddress && ` · IP ${l.ipAddress}`}
                      </p>
                      {open === l.id && (
                        <div className="grid-2">
                          <pre className="json">{l.before ? JSON.stringify(JSON.parse(l.before), null, 2) : '—'}</pre>
                          <pre className="json">{l.after ? JSON.stringify(JSON.parse(l.after), null, 2) : '—'}</pre>
                        </div>
                      )}
                    </div>
                  </button>
                </li>
              ))}
            </ul>
            <Pagination page={page} totalPages={logs.data.totalPages} onChange={setPage} />
          </>
        )}
      </Card>
    </div>
  )
}
