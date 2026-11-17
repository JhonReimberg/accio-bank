import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Amount, Card, Empty, ErrorBox, Loading } from '../components/ui'
import { api } from '../lib/api'
import { brl, dayLabel } from '../lib/format'
import { keys, useAccount } from '../lib/queries'
import type { Paged, StatementItem } from '../lib/types'

const HIDE_KEY = 'vela.hideBalance'

function readHidden() {
  try {
    return localStorage.getItem(HIDE_KEY) === '1'
  } catch {
    return false
  }
}

const shortcuts = [
  { to: '/pix/pagar', label: 'Pagar com PIX', icon: '↗' },
  { to: '/pix/receber', label: 'Receber', icon: '↙' },
  { to: '/extrato', label: 'Extrato', icon: '≡' },
  { to: '/depositar', label: 'Depositar', icon: '+' },
]

export default function HomePage() {
  const account = useAccount()
  const latest = useQuery({
    queryKey: [...keys.statement, 'latest'],
    queryFn: () => api<Paged<StatementItem>>('/account/statement?pageSize=5'),
  })
  const [hidden, setHidden] = useState(readHidden)

  function toggle() {
    setHidden(!hidden)
    try {
      localStorage.setItem(HIDE_KEY, hidden ? '0' : '1')
    } catch {
      /* private mode: preference lasts until reload */
    }
  }

  if (account.isPending) return <Loading />
  if (account.isError) return <ErrorBox error={account.error} retry={account.refetch} />
  const a = account.data

  return (
    <div className="stack-lg">
      <header>
        <p className="muted">Olá,</p>
        <h1>{a.name.split(' ')[0]}</h1>
      </header>

      <section className="card balance" aria-label="Saldo">
        <div className="row-between">
          <span>Saldo disponível</span>
          <button className="btn btn-on-dark" onClick={toggle} aria-pressed={hidden}>
            {hidden ? 'Mostrar' : 'Ocultar'}
          </button>
        </div>
        <p className="balance-value">{hidden ? 'R$ ••••' : brl(a.availableBalance)}</p>
        {a.blockedBalance > 0 && !hidden && <p className="small">+ {brl(a.blockedBalance)} bloqueado</p>}
        <p className="small">
          Conta {a.number} · {a.tier}
          {a.status === 'Blocked' && ' · conta bloqueada'}
        </p>
      </section>

      <nav className="shortcuts" aria-label="Atalhos">
        {shortcuts.map((s) => (
          <Link key={s.to} to={s.to} className="shortcut">
            <span aria-hidden>{s.icon}</span>
            {s.label}
          </Link>
        ))}
      </nav>

      <Card>
        <div className="row-between">
          <h2>Últimas movimentações</h2>
          <Link to="/extrato">Ver tudo</Link>
        </div>
        {latest.isPending ? (
          <Loading />
        ) : latest.isError ? (
          <ErrorBox error={latest.error} retry={latest.refetch} />
        ) : latest.data.items.length === 0 ? (
          <Empty>Nenhuma movimentação ainda. Que tal fazer um depósito?</Empty>
        ) : (
          <ul className="list">
            {latest.data.items.map((t) => (
              <li key={t.transactionId + t.direction}>
                <div>
                  <p>{t.description}</p>
                  <p className="muted small">{dayLabel(t.createdAt)}</p>
                </div>
                <Amount value={t.amount} hidden={hidden} />
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}
