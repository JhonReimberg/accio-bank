import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Alert, Button, Card, Empty, ErrorBox, Loading, PageHeader, PinDialog } from '../../components/ui'
import { api, download } from '../../lib/api'
import { brl, dateOnly, dateTime, percent } from '../../lib/format'
import { keys, useMoneyMoved } from '../../lib/queries'
import type { Installment, Loan } from '../../lib/types'

const statusLabel = { Pending: 'A vencer', Overdue: 'Em atraso', Paid: 'Paga' }

export default function LoansPage() {
  const loans = useQuery({ queryKey: [...keys.account, 'loans'], queryFn: () => api<Loan[]>('/credit/loans') })

  return (
    <div className="stack-lg">
      <PageHeader title="Meus empréstimos" back="/credito" />
      {loans.isPending ? (
        <Loading />
      ) : loans.isError ? (
        <ErrorBox error={loans.error} retry={loans.refetch} />
      ) : loans.data.length === 0 ? (
        <Card>
          <Empty>
            Você ainda não tem empréstimos. <Link to="/credito">Ver sua oferta</Link>
          </Empty>
        </Card>
      ) : (
        loans.data.map((loan) => <LoanCard key={loan.id} loan={loan} />)
      )}
    </div>
  )
}

function LoanCard({ loan }: { loan: Loan }) {
  const moneyMoved = useMoneyMoved()
  const [paying, setPaying] = useState<Installment | null>(null)
  const pay = useMutation({
    mutationFn: ({ number, pin }: { number: number; pin: string }) =>
      api(`/credit/loans/${loan.id}/installments/${number}/pay`, { body: { pin }, idempotent: true }),
    onSuccess: () => {
      setPaying(null)
      moneyMoved()
    },
  })
  const paid = loan.installments.filter((i) => i.status === 'Paid').length

  return (
    <Card className="stack">
      <div className="row-between">
        <div>
          <h2>{brl(loan.amount)}</h2>
          <p className="muted small">
            Contratado em {dateTime(loan.contractedAt)} · {loan.installmentCount}x de {brl(loan.installmentAmount)} · {percent(loan.monthlyRate)} a.m. · CET{' '}
            {percent(loan.cetYearly)} a.a.
          </p>
        </div>
        <span className={`badge-soft ${loan.status === 'PaidOff' ? 'ok' : ''}`}>{loan.status === 'PaidOff' ? 'Quitado' : `${paid}/${loan.installmentCount} pagas`}</span>
      </div>
      <progress max={loan.installmentCount} value={paid} aria-label={`${paid} de ${loan.installmentCount} parcelas pagas`} />
      <p className="small">Saldo devedor: {brl(loan.outstandingPrincipal)}</p>

      <div className="table-wrap">
        <table className="table">
          <thead>
            <tr>
              <th>Nº</th>
              <th>Vencimento</th>
              <th>Parcela</th>
              <th>Situação</th>
              <th>Hoje</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {loan.installments.map((i) => (
              <tr key={i.number} className={i.status === 'Overdue' ? 'row-alert' : ''}>
                <td>{i.number}</td>
                <td>{dateOnly(i.dueDate)}</td>
                <td>{brl(i.amount)}</td>
                <td>{statusLabel[i.status]}</td>
                <td>{i.status === 'Paid' ? brl(i.paidAmount ?? 0) : brl(i.amountDueToday ?? i.amount)}</td>
                <td>
                  {i.status !== 'Paid' && (
                    <Button variant="ghost" onClick={() => (pay.reset(), setPaying(i))}>
                      {i.status === 'Overdue' ? 'Pagar' : 'Antecipar'}
                    </Button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <p className="muted small">Atraso: multa de 2% e juros de mora por dia. Antecipação: desconto proporcional dos juros.</p>
      <Button variant="ghost" onClick={() => download(`/credit/loans/${loan.id}/contract`, `contrato-${loan.id}.pdf`)}>
        Baixar contrato (PDF)
      </Button>
      <Alert kind="error">{pay.error && !paying ? pay.error.message : ''}</Alert>
      <PinDialog
        open={paying !== null}
        title={paying ? `Pagar parcela ${paying.number}: ${brl(paying.amountDueToday ?? paying.amount)}` : ''}
        busy={pay.isPending}
        error={pay.error?.message}
        onConfirm={(pin) => paying && pay.mutate({ number: paying.number, pin })}
        onCancel={() => setPaying(null)}
      />
    </Card>
  )
}
