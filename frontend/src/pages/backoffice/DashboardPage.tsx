import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Card, ErrorBox, Loading, PageHeader } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, dateOnly, percent } from '../../lib/format'
import type { Dashboard } from '../../lib/types'

export default function DashboardPage() {
  const dashboard = useQuery({ queryKey: ['backoffice', 'dashboard'], queryFn: () => api<Dashboard>('/admin/dashboard') })
  if (dashboard.isPending) return <Loading />
  if (dashboard.isError) return <ErrorBox error={dashboard.error} retry={dashboard.refetch} />
  const d = dashboard.data
  const max = Math.max(1, ...d.pixByDay.map((x) => x.amount))

  return (
    <div className="stack-lg">
      <PageHeader title="Painel" />
      <div className="kpis">
        <Kpi label="Clientes" value={String(d.customers)} hint={`${d.activeAccounts} contas ativas`} />
        <Kpi label="KYC pendente" value={String(d.pendingKyc)} hint={<Link to="/backoffice/kyc">Abrir fila</Link>} />
        <Kpi label="PIX (30 dias)" value={brl(d.pixVolume30Days)} hint={`${d.pixCount30Days} transações`} />
        <Kpi label="Carteira de crédito" value={brl(d.creditPortfolio)} hint={`${d.activeLoans} empréstimos ativos`} />
        <Kpi
          label="Inadimplência"
          value={percent(d.delinquencyRate)}
          hint={`${d.overdueInstallments} parcelas · ${brl(d.overdueAmount)}`}
          alert={d.overdueInstallments > 0}
        />
        <Kpi label="Cheque especial em uso" value={brl(d.overdraftUsed)} />
        <Kpi label="Ledger" value={d.ledgerBalanced ? 'Conciliado' : 'DIVERGENTE'} alert={!d.ledgerBalanced} hint="débitos = créditos" />
      </div>
      <Card className="stack">
        <h2>Volume de PIX por dia (30 dias)</h2>
        {d.pixByDay.length === 0 ? (
          <p className="muted">Sem PIX no período.</p>
        ) : (
          <ul className="bars" aria-label="Volume de PIX por dia">
            {d.pixByDay.map((x) => (
              <li key={x.day}>
                <span className="bar-label">{dateOnly(x.day).slice(0, 5)}</span>
                <span className="bar-track">
                  <span className="bar" style={{ width: `${(x.amount / max) * 100}%` }} />
                </span>
                <span className="bar-value">
                  {brl(x.amount)} <span className="muted small">({x.count})</span>
                </span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  )
}

function Kpi({ label, value, hint, alert }: { label: string; value: string; hint?: ReactNode; alert?: boolean }) {
  return (
    <div className={`card kpi ${alert ? 'kpi-alert' : ''}`}>
      <span className="muted small">{label}</span>
      <strong className="big">{value}</strong>
      {hint && <span className="muted small">{hint}</span>}
    </div>
  )
}
