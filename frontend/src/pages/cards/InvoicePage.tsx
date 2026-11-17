import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Empty, ErrorBox, Field, Loading, PageHeader, PinDialog } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, dateOnly, parseAmount } from '../../lib/format'
import { keys, useMoneyMoved } from '../../lib/queries'
import type { CreditLine, Invoice } from '../../lib/types'

const creditLineKey = keys.creditLine

const statusLabel: Record<Invoice['status'], string> = { Open: 'Aberta', Closed: 'Fechada', Paid: 'Paga', Revolved: 'Paga parcialmente (rotativo)' }

export default function InvoicePage() {
  const line = useQuery({ queryKey: creditLineKey, queryFn: async () => (await api<CreditLine | undefined>('/cards/credit-line')) ?? null })
  const [index, setIndex] = useState<number | null>(null)

  if (line.isPending) return <Loading />
  if (line.isError) return <ErrorBox error={line.error} retry={line.refetch} />
  if (!line.data) return <Empty>Você ainda não tem cartão de crédito.</Empty>
  const invoices = line.data.invoices
  if (invoices.length === 0)
    return (
      <div className="stack-lg">
        <PageHeader title="Fatura" back="/cartoes" />
        <Empty>Nenhuma compra no crédito ainda.</Empty>
      </div>
    )
  // Default: the oldest invoice still to pay (closed first, then the open one).
  const current = index ?? Math.max(0, invoices.findIndex((i) => i.status === 'Closed' || i.status === 'Open'))
  const invoice = invoices[current]

  return (
    <div className="stack-lg">
      <PageHeader title="Fatura" back="/cartoes" />
      <div className="tabs" role="tablist" aria-label="Faturas">
        {invoices.map((i, k) => (
          <button key={i.id} role="tab" aria-selected={k === current} className={k === current ? 'tab-active' : ''} onClick={() => setIndex(k)}>
            {i.dueDate.split('-').reverse().slice(1).join('/')}
          </button>
        ))}
      </div>
      <Card className="stack">
        <div className="row-between">
          <div>
            <p className="muted small">{statusLabel[invoice.status]}</p>
            <p className="big">{brl(invoice.total)}</p>
          </div>
          <div className="text-right small">
            <p>Fecha em {dateOnly(invoice.closingDate)}</p>
            <p>Vence em {dateOnly(invoice.dueDate)}</p>
          </div>
        </div>
        {invoice.paidAmount > 0 && <p className="small amount-in">Pago: {brl(invoice.paidAmount)}</p>}
        <ul className="list">
          {invoice.items.map((item, k) => (
            <li key={k} className="row-between">
              <span>{item.description}</span>
              <span className={item.type === 'Purchase' ? '' : 'amount-out'}>{brl(item.amount)}</span>
            </li>
          ))}
        </ul>
      </Card>
      {(invoice.status === 'Open' || invoice.status === 'Closed') && invoice.remaining > 0 && <PayInvoice invoice={invoice} />}
    </div>
  )
}

function PayInvoice({ invoice }: { invoice: Invoice }) {
  const client = useQueryClient()
  const moneyMoved = useMoneyMoved()
  const [amount, setAmount] = useState('')
  const [asking, setAsking] = useState(false)
  const value = parseAmount(amount)
  const pay = useMutation({
    mutationFn: (pin: string) => api(`/cards/invoices/${invoice.id}/payments`, { method: 'POST', body: { amount: value, pin }, idempotent: true }),
    onSuccess: () => {
      setAsking(false)
      setAmount('')
      moneyMoved()
      client.invalidateQueries({ queryKey: creditLineKey })
    },
  })
  const fill = (v: number) => setAmount(v.toFixed(2).replace('.', ','))

  return (
    <Card>
      <form
        className="stack"
        onSubmit={(e) => {
          e.preventDefault()
          pay.reset()
          setAsking(true)
        }}
      >
        <h2>Pagar fatura</h2>
        <div className="chips">
          <button type="button" className="chip" onClick={() => fill(invoice.remaining)}>
            Total {brl(invoice.remaining)}
          </button>
          {invoice.status === 'Closed' && invoice.minimumPayment < invoice.remaining && (
            <button type="button" className="chip" onClick={() => fill(Math.max(invoice.minimumPayment - invoice.paidAmount, 0.01))}>
              Mínimo {brl(Math.max(invoice.minimumPayment - invoice.paidAmount, 0.01))}
            </button>
          )}
        </div>
        <Field label="Outro valor (R$)" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} />
        <p className="muted small">O que não for pago até o vencimento vai para a próxima fatura com juros do rotativo (12% a.m.), e multa de 2% se ficar abaixo do mínimo.</p>
        <Button disabled={Number.isNaN(value) || value <= 0}>Pagar com saldo da conta</Button>
        <Alert kind="success">{pay.isSuccess ? 'Pagamento feito.' : ''}</Alert>
        <PinDialog open={asking} busy={pay.isPending} error={pay.error?.message} onConfirm={(pin) => pay.mutate(pin)} onCancel={() => setAsking(false)} />
      </form>
    </Card>
  )
}
