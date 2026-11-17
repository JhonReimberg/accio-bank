import { useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { Alert, Amount, Button, Card, Empty, ErrorBox, Field, Loading, PageHeader, Pagination } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, dateOnly, dateTime, kindLabel } from '../../lib/format'
import type { Customer360, CustomerListItem, Paged } from '../../lib/types'

const statusLabel: Record<string, string> = { PendingReview: 'Em análise', Active: 'Ativa', Blocked: 'Bloqueada', Closed: 'Encerrada' }

export function CustomersPage() {
  const [search, setSearch] = useState('')
  const [term, setTerm] = useState('')
  const [page, setPage] = useState(1)
  const list = useQuery({
    queryKey: ['backoffice', 'customers', term, page],
    queryFn: () => api<Paged<CustomerListItem>>(`/admin/customers?search=${encodeURIComponent(term)}&page=${page}&pageSize=20`),
    placeholderData: keepPreviousData,
  })

  return (
    <div className="stack-lg">
      <PageHeader title="Clientes" />
      <Card>
        <form
          className="row"
          onSubmit={(e) => {
            e.preventDefault()
            setPage(1)
            setTerm(search)
          }}
        >
          <Field label="Buscar por nome, e-mail ou CPF" value={search} onChange={(e) => setSearch(e.target.value)} />
          <Button variant="ghost">Buscar</Button>
        </form>
      </Card>
      <Card>
        {list.isPending ? (
          <Loading />
        ) : list.isError ? (
          <ErrorBox error={list.error} retry={list.refetch} />
        ) : list.data.items.length === 0 ? (
          <Empty>Nenhum cliente encontrado.</Empty>
        ) : (
          <>
            <ul className="list list-clickable">
              {list.data.items.map((c) => (
                <li key={c.userId}>
                  <Link to={`/backoffice/clientes/${c.userId}`} className="row-link">
                    <div>
                      <p>
                        <strong>{c.name}</strong> · {c.email}
                      </p>
                      <p className="muted small">
                        CPF {c.maskedCpf ?? '—'} · {c.accountStatus ? statusLabel[c.accountStatus] : 'sem conta'} · {c.tier ?? ''} · KYC {c.kycStatus}
                      </p>
                    </div>
                    <span className="badge-soft">score {c.score ?? '—'}</span>
                  </Link>
                </li>
              ))}
            </ul>
            <Pagination page={page} totalPages={list.data.totalPages} onChange={setPage} />
          </>
        )}
      </Card>
    </div>
  )
}

export function CustomerPage() {
  const { userId } = useParams()
  const client = useQueryClient()
  const view = useQuery({ queryKey: ['backoffice', 'customer', userId], queryFn: () => api<Customer360>(`/admin/customers/${userId}`) })
  const refresh = () => client.invalidateQueries({ queryKey: ['backoffice'] })
  const [reason, setReason] = useState('')
  const [limit, setLimit] = useState('')
  const action = useMutation({
    mutationFn: (run: () => Promise<unknown>) => run(),
    onSuccess: () => (setReason(''), setLimit(''), refresh()),
  })

  if (view.isPending) return <Loading />
  if (view.isError) return <ErrorBox error={view.error} retry={view.refetch} />
  const c = view.data
  const a = c.account

  return (
    <div className="stack-lg">
      <PageHeader title={c.name} back="/backoffice/clientes" />
      <div className="grid-auto">
        <Card className="stack">
          <h2>Cadastro</h2>
          <dl className="summary">
            <dt>E-mail</dt>
            <dd>{c.email}</dd>
            <dt>CPF</dt>
            <dd>{c.cpf}</dd>
            <dt>Celular</dt>
            <dd>{c.phone}</dd>
            <dt>Nascimento</dt>
            <dd>{c.birthDate && dateOnly(c.birthDate)}</dd>
            <dt>Renda</dt>
            <dd>
              {brl(c.monthlyIncome ?? 0)} · {c.occupation}
            </dd>
            <dt>KYC</dt>
            <dd>{c.kycStatus}</dd>
            <dt>Cliente desde</dt>
            <dd>{dateTime(c.createdAt)}</dd>
          </dl>
        </Card>

        {a && c.accountId && (
          <Card className="stack">
            <h2>Conta {a.number}</h2>
            <dl className="summary">
              <dt>Situação</dt>
              <dd>
                {statusLabel[a.status]} {a.statusReason && `(${a.statusReason})`}
              </dd>
              <dt>Nível</dt>
              <dd>{a.tier}</dd>
              <dt>Saldo</dt>
              <dd>{brl(a.balance)}</dd>
              <dt>Bloqueado</dt>
              <dd>{brl(a.blockedBalance)}</dd>
              <dt>Cheque especial</dt>
              <dd>
                {brl(a.overdraftUsed)} de {brl(a.overdraftLimit)}
              </dd>
            </dl>
            <Field label="Motivo da ação (obrigatório)" maxLength={200} value={reason} onChange={(e) => setReason(e.target.value)} />
            <div className="row">
              {a.status === 'Active' && (
                <Button variant="danger" disabled={!reason.trim()} onClick={() => action.mutate(() => api(`/admin/accounts/${c.accountId}/block`, { body: { reason } }))}>
                  Bloquear conta
                </Button>
              )}
              {a.status === 'Blocked' && (
                <Button variant="ghost" onClick={() => action.mutate(() => api(`/admin/accounts/${c.accountId}/unblock`, { method: 'POST' }))}>
                  Desbloquear
                </Button>
              )}
            </div>
            <div className="row">
              <Field label="Novo limite de cheque especial" inputMode="decimal" value={limit} onChange={(e) => setLimit(e.target.value)} />
              <Button
                variant="ghost"
                disabled={!reason.trim() || limit === ''}
                onClick={() =>
                  action.mutate(() =>
                    api(`/admin/accounts/${c.accountId}/overdraft-limit`, { method: 'PUT', body: { limit: Number(limit.replace(',', '.')), reason } }),
                  )
                }
              >
                Ajustar
              </Button>
            </div>
            <Alert kind="error">{action.error?.message}</Alert>
          </Card>
        )}

        {c.score && (
          <Card className="stack">
            <div className="row-between">
              <h2>Score</h2>
              <p className="big">{c.score.score}</p>
            </div>
            <ul className="list">
              {c.score.factors.map((f) => (
                <li key={f.code}>
                  <span className="small">{f.description}</span>
                  <span className={f.points < 0 ? 'amount-out' : 'amount-in'}>{f.points}</span>
                </li>
              ))}
            </ul>
            <p className="muted small">Histórico: {c.score.history.map((h) => h.score).join(' · ')}</p>
          </Card>
        )}
      </div>

      <Card className="stack">
        <h2>Empréstimos</h2>
        {c.loans.length === 0 ? (
          <Empty>Nenhum empréstimo.</Empty>
        ) : (
          <ul className="list">
            {c.loans.map((l) => (
              <li key={l.id} className={l.overdueInstallments > 0 ? 'unread' : ''}>
                <div>
                  <p>
                    <strong>{brl(l.amount)}</strong> em {l.installmentCount}x · {dateTime(l.contractedAt)}
                  </p>
                  <p className="muted small">
                    {l.paidInstallments} pagas · {l.overdueInstallments} em atraso · saldo devedor {brl(l.outstandingPrincipal)}
                  </p>
                </div>
                <span className="badge-soft">{l.status === 'PaidOff' ? 'Quitado' : 'Ativo'}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>

      <Card className="stack">
        <h2>Últimas transações</h2>
        <ul className="list">
          {c.recentTransactions.map((t) => (
            <li key={t.transactionId + t.amount}>
              <div>
                <p>{t.description}</p>
                <p className="muted small">
                  {kindLabel[t.kind]} · {dateTime(t.createdAt)}
                </p>
              </div>
              <Amount value={t.amount} />
            </li>
          ))}
        </ul>
      </Card>
    </div>
  )
}
