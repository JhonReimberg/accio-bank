import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { Amount, CircleActions, ErrorBox, ListRow, Loading, PageHeader, RowGroup } from '../components/ui'
import { api } from '../lib/api'
import { brl, dayLabel, percent } from '../lib/format'
import { useHidden } from '../lib/hideBalance'
import { keys, useAccount } from '../lib/queries'
import type { Investment, Paged, StatementItem } from '../lib/types'

/** "Conta": Usar (money to spend) and Guardar (vaults), like the reference app. */
export default function AccountPage() {
  const [params, setParams] = useSearchParams()
  const tab = params.get('aba') === 'guardar' ? 'guardar' : 'usar'

  return (
    <div className="stack-lg">
      <PageHeader title="Conta" back="/" />
      <div className="tabs" role="tablist">
        {(['usar', 'guardar'] as const).map((t) => (
          <button key={t} role="tab" aria-selected={tab === t} className={tab === t ? 'tab-active' : ''} onClick={() => setParams(t === 'usar' ? {} : { aba: t }, { replace: true })}>
            {t === 'usar' ? 'Usar' : 'Guardar'}
          </button>
        ))}
      </div>
      {tab === 'usar' ? <Use /> : <Save />}
    </div>
  )
}

function Use() {
  const account = useAccount()
  const hidden = useHidden()
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const latest = useQuery({ queryKey: [...keys.statement, 'latest-10'], queryFn: () => api<Paged<StatementItem>>('/account/statement?pageSize=8') })
  if (account.isPending) return <Loading />
  if (account.isError) return <ErrorBox error={account.error} retry={account.refetch} />
  const a = account.data

  return (
    <>
      <div className="center-value">
        <p className="muted">Saldo disponível</p>
        <p className="hero-value">{hidden ? 'R$ ••••' : brl(a.availableBalance)}</p>
        {a.blockedBalance > 0 && <p className="muted small">+ {hidden ? '••••' : brl(a.blockedBalance)} bloqueado por segurança</p>}
      </div>

      <CircleActions
        label="Movimentar"
        items={[
          { to: '/depositar', icon: '↓', label: 'Trazer dinheiro' },
          { to: '/pix/pagar', icon: '⇄', label: 'Transferir' },
          { to: '/pagamentos/boleto', icon: '▥', label: 'Pagar' },
          { to: '/investimentos/novo', icon: '↗', label: 'Investir' },
        ]}
      />

      <RowGroup title="Próximos pagamentos">
        <ListRow to="/pix/agendados" icon="◷" title="PIX agendados" text="Únicos e recorrentes" />
        <ListRow to="/pagamentos/boleto" icon="▥" title="Boletos agendados" text="Pague hoje ou programe" />
      </RowGroup>

      <section className="stack">
        <div className="row-between">
          <h2>Histórico</h2>
          <Link to="/extrato">Filtrar</Link>
        </div>
        <form
          role="search"
          onSubmit={(e) => {
            e.preventDefault()
            navigate(`/extrato?search=${encodeURIComponent(search)}`)
          }}
        >
          <input className="search" type="search" placeholder="Buscar no extrato" aria-label="Buscar no extrato" value={search} onChange={(e) => setSearch(e.target.value)} />
        </form>
        {latest.isPending ? (
          <Loading />
        ) : latest.isError ? (
          <ErrorBox error={latest.error} retry={latest.refetch} />
        ) : (
          <ul className="list">
            {latest.data.items.map((t) => (
              <li key={t.transactionId + t.direction}>
                <div className="tx">
                  <span className={`tx-icon ${t.amount < 0 ? 'out' : ''}`} aria-hidden>
                    {t.amount < 0 ? '↗' : '↙'}
                  </span>
                  <div>
                    <p>{t.description}</p>
                    <p className="muted small">{dayLabel(t.createdAt)}</p>
                  </div>
                </div>
                <Amount value={t.amount} hidden={hidden} />
              </li>
            ))}
          </ul>
        )}
        <Link to="/extrato" className="btn btn-ghost">
          Ver extrato completo
        </Link>
      </section>
    </>
  )
}

function Save() {
  const hidden = useHidden()
  const list = useQuery({ queryKey: [...keys.account, 'investments'], queryFn: () => api<Investment[]>('/investments') })
  if (list.isPending) return <Loading />
  if (list.isError) return <ErrorBox error={list.error} retry={list.refetch} />
  const vaults = list.data.filter((i) => i.type === 'Caixinha')
  const total = vaults.reduce((s, i) => s + i.value, 0)
  const income = vaults.reduce((s, i) => s + i.income, 0)

  return (
    <>
      <div className="center-value">
        <p className="hero-value">{hidden ? 'R$ ••••' : brl(total)}</p>
        <p className="amount-in small">▲ {hidden ? '••••' : brl(income)} de rendimento</p>
      </div>

      <div className="row-between">
        <h2>Cofres</h2>
        <Link to="/investimentos/novo?tipo=Caixinha">Criar cofre</Link>
      </div>
      <div className="vault-grid">
        {vaults.map((v) => (
          <Link key={v.id} to={`/investimentos/${v.id}`} className="vault">
            <span className="muted small">{v.name}</span>
            <strong>{hidden ? 'R$ ••••' : brl(v.value)}</strong>
            <span className="muted small">{percent(v.cdiPercent)} do CDI</span>
            {v.goal !== null && v.goalProgress !== null && <progress max={1} value={Math.min(v.goalProgress, 1)} aria-label={`Meta de ${v.name}`} />}
            <span className="vault-art" aria-hidden>
              ◫
            </span>
          </Link>
        ))}
        <Link to="/investimentos/novo?tipo=Caixinha" className="vault vault-new">
          <span aria-hidden>+</span>
          Novo cofre
        </Link>
      </div>

      <Link to="/investimentos" className="banner">
        <span>
          <strong>Seu dinheiro pode render mais</strong>
          <span>Conheça os CDBs e faça seu dinheiro crescer.</span>
        </span>
        <span className="banner-icon" aria-hidden>
          ↗
        </span>
      </Link>
    </>
  )
}
