import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, CircleActions, Empty, ErrorBox, Field, ListRow, Loading, PageHeader, PinDialog, RowGroup, Select } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, dateOnly, maskPhone, parseAmount, todayInBrasilia } from '../../lib/format'
import { keys, useMoneyMoved } from '../../lib/queries'
import type { Boleto, BoletoInfo, BoletoPayment } from '../../lib/types'

const boletosKey = [...keys.account, 'boletos']
/** Payments hub: each kind of payment has its own screen. */
export default function PaymentsPage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Pagamentos" back="/" />
      <CircleActions
        label="Pagamentos"
        items={[
          { to: '/pagamentos/boleto', icon: '▥', label: 'Pagar boleto' },
          { to: '/pagamentos/cobrar', icon: '↙', label: 'Cobrar com boleto' },
          { to: '/recarga', icon: '▯', label: 'Recarga de celular' },
          { to: '/pix/pagar', icon: '◈', label: 'Pagar com PIX' },
        ]}
      />
      <RowGroup title="Boletos">
        <ListRow to="/pagamentos/boleto" icon="▥" title="Pagar boleto" text="Pelo código de barras, hoje ou agendado" />
        <ListRow to="/pagamentos/cobrar" icon="↙" title="Cobrar com boleto" text="Emita um boleto para receber" />
      </RowGroup>
      <RowGroup title="Celular">
        <ListRow to="/recarga" icon="▯" title="Recarga de celular" text="Todas as operadoras" />
      </RowGroup>
    </div>
  )
}

export function PayBoletoPage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Pagar boleto" back="/pagamentos" />
      <PayBoleto />
    </div>
  )
}

export function IssueBoletoPage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Cobrar com boleto" back="/pagamentos" />
      <IssueBoleto />
    </div>
  )
}

export function RechargePage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Recarga de celular" />
      <Recharge />
    </div>
  )
}

const paymentStatus: Record<BoletoPayment['status'], string> = { Scheduled: 'Agendado', Paid: 'Pago', Failed: 'Não pago', Cancelled: 'Cancelado' }

function PayBoleto() {
  const client = useQueryClient()
  const moneyMoved = useMoneyMoved()
  const [code, setCode] = useState('')
  const [info, setInfo] = useState<BoletoInfo | null>(null)
  const [amount, setAmount] = useState('')
  const [date, setDate] = useState(todayInBrasilia())
  const [asking, setAsking] = useState(false)
  const payments = useQuery({ queryKey: [...boletosKey, 'payments'], queryFn: () => api<BoletoPayment[]>('/boletos/payments') })
  const inspect = useMutation({
    mutationFn: () => api<BoletoInfo>(`/boletos/inspect?code=${encodeURIComponent(code)}`),
    onSuccess: setInfo,
  })
  const pay = useMutation({
    mutationFn: (pin: string) =>
      api<BoletoPayment>('/boletos/payments', {
        method: 'POST',
        body: { code, amount: info?.amount ? null : parseAmount(amount), scheduledFor: date, pin },
        idempotent: true,
      }),
    onSuccess: () => {
      setAsking(false)
      setInfo(null)
      setCode('')
      moneyMoved()
      client.invalidateQueries({ queryKey: boletosKey })
    },
  })
  const cancel = useMutation({
    mutationFn: (id: string) => api(`/boletos/payments/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: boletosKey }),
  })

  return (
    <>
      <Card>
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault()
            if (!info) inspect.mutate()
            else {
              pay.reset()
              setAsking(true)
            }
          }}
        >
          <Field
            label="Linha digitável ou código de barras"
            inputMode="numeric"
            required
            value={code}
            onChange={(e) => {
              setCode(e.target.value)
              setInfo(null)
            }}
          />
          <Alert kind="error">{inspect.error?.message}</Alert>
          {info && (
            <>
              <dl className="summary">
                <dt>Beneficiário</dt>
                <dd>{info.beneficiaryName}</dd>
                <dt>Valor</dt>
                <dd>{info.amount > 0 ? brl(info.amount) : 'Em aberto'}</dd>
                <dt>Vencimento</dt>
                <dd>{info.dueDate ? dateOnly(info.dueDate) : '—'}</dd>
              </dl>
              {info.overdue && <Alert kind="info">Boleto vencido: o pagamento pode não ser aceito pelo beneficiário.</Alert>}
              {info.amount === 0 && <Field label="Valor a pagar (R$)" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} />}
              <Field
                label="Data do pagamento"
                type="date"
                required
                min={todayInBrasilia()}
                max={info.dueDate ?? undefined}
                value={date}
                onChange={(e) => setDate(e.target.value)}
              />
            </>
          )}
          <Button busy={inspect.isPending}>{!info ? 'Continuar' : date === todayInBrasilia() ? 'Pagar agora' : 'Agendar'}</Button>
          <Alert kind="success">{pay.data ? (pay.data.status === 'Paid' ? 'Boleto pago.' : `Pagamento agendado para ${dateOnly(pay.data.scheduledFor)}.`) : ''}</Alert>
          <PinDialog open={asking} busy={pay.isPending} error={pay.error?.message} onConfirm={(pin) => pay.mutate(pin)} onCancel={() => setAsking(false)} />
        </form>
      </Card>
      <Card className="stack">
        <h2>Pagamentos de boletos</h2>
        {payments.isPending ? (
          <Loading />
        ) : payments.isError ? (
          <ErrorBox error={payments.error} retry={payments.refetch} />
        ) : payments.data.length === 0 ? (
          <Empty>Nenhum boleto pago ainda.</Empty>
        ) : (
          <ul className="list">
            {payments.data.map((p) => (
              <li key={p.id} className="row-between">
                <div>
                  <p>{p.beneficiaryName}</p>
                  <p className="muted small">
                    {paymentStatus[p.status]} · {dateOnly(p.scheduledFor)}
                    {p.failureReason ? ` · ${p.failureReason}` : ''}
                  </p>
                </div>
                <div className="text-right">
                  <p>{brl(p.amount)}</p>
                  {p.status === 'Scheduled' && (
                    <button className="btn btn-ghost small" onClick={() => cancel.mutate(p.id)} disabled={cancel.isPending}>
                      Cancelar
                    </button>
                  )}
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </>
  )
}

const boletoStatus: Record<Boleto['status'], string> = { Open: 'Em aberto', Paid: 'Pago', Cancelled: 'Cancelado' }

function IssueBoleto() {
  const client = useQueryClient()
  const [amount, setAmount] = useState('')
  const [due, setDue] = useState(todayInBrasilia())
  const [description, setDescription] = useState('')
  const [copied, setCopied] = useState<string | null>(null)
  const issued = useQuery({ queryKey: [...boletosKey, 'issued'], queryFn: () => api<Boleto[]>('/boletos') })
  const issue = useMutation({
    mutationFn: () => api<Boleto>('/boletos', { method: 'POST', body: { amount: parseAmount(amount), dueDate: due, description: description || null } }),
    onSuccess: () => {
      setAmount('')
      setDescription('')
      client.invalidateQueries({ queryKey: boletosKey })
    },
  })
  const cancel = useMutation({
    mutationFn: (id: string) => api(`/boletos/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: boletosKey }),
  })
  async function copy(b: Boleto) {
    try {
      await navigator.clipboard.writeText(b.digitableLine.replace(/\D/g, ''))
      setCopied(b.id)
    } catch {
      setCopied(null)
    }
  }

  return (
    <>
      <Card>
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault()
            issue.mutate()
          }}
        >
          <p className="muted small">Gere um boleto para receber de qualquer pessoa. Quando for pago, o dinheiro cai na sua conta.</p>
          <Field label="Valor (R$)" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} />
          <Field label="Vencimento" type="date" required min={todayInBrasilia()} value={due} onChange={(e) => setDue(e.target.value)} />
          <Field label="Descrição (opcional)" maxLength={140} value={description} onChange={(e) => setDescription(e.target.value)} />
          <Alert kind="error">{issue.error?.message}</Alert>
          <Button busy={issue.isPending} disabled={Number.isNaN(parseAmount(amount))}>
            Gerar boleto
          </Button>
        </form>
      </Card>
      <Card className="stack">
        <h2>Boletos emitidos</h2>
        {issued.isPending ? (
          <Loading />
        ) : issued.isError ? (
          <ErrorBox error={issued.error} retry={issued.refetch} />
        ) : issued.data.length === 0 ? (
          <Empty>Nenhum boleto emitido.</Empty>
        ) : (
          <ul className="list">
            {issued.data.map((b) => (
              <li key={b.id} className="stack-sm">
                <div className="row-between">
                  <span>
                    {brl(b.amount)} · vence {dateOnly(b.dueDate)}
                    {b.description ? ` · ${b.description}` : ''}
                  </span>
                  <span className={`badge-soft ${b.status === 'Paid' ? 'ok' : ''}`}>{boletoStatus[b.status]}</span>
                </div>
                <p className="mono small break">{b.digitableLine}</p>
                {b.status === 'Open' && (
                  <div className="row">
                    <button className="btn btn-ghost small" onClick={() => copy(b)}>
                      {copied === b.id ? 'Copiado!' : 'Copiar linha digitável'}
                    </button>
                    <button className="btn btn-ghost small" onClick={() => cancel.mutate(b.id)} disabled={cancel.isPending}>
                      Cancelar
                    </button>
                  </div>
                )}
              </li>
            ))}
          </ul>
        )}
      </Card>
    </>
  )
}

function Recharge() {
  const moneyMoved = useMoneyMoved()
  const options = useQuery({ queryKey: ['recharge-options'], queryFn: () => api<{ operators: string[]; amounts: number[] }>('/recharges/options') })
  const [operator, setOperator] = useState('')
  const [phone, setPhone] = useState('')
  const [amount, setAmount] = useState<number | null>(null)
  const [asking, setAsking] = useState(false)
  const recharge = useMutation({
    mutationFn: (pin: string) =>
      api<{ phone: string; amount: number; operator: string }>('/recharges', {
        method: 'POST',
        body: { operator: operator || options.data?.operators[0], phone, amount, pin },
        idempotent: true,
      }),
    onSuccess: () => {
      setAsking(false)
      moneyMoved()
    },
  })
  if (options.isPending) return <Loading />
  if (options.isError) return <ErrorBox error={options.error} retry={options.refetch} />

  return (
    <Card>
      <form
        className="stack"
        onSubmit={(e) => {
          e.preventDefault()
          recharge.reset()
          setAsking(true)
        }}
      >
        <Select label="Operadora" value={operator || options.data.operators[0]} onChange={(e) => setOperator(e.target.value)}>
          {options.data.operators.map((o) => (
            <option key={o}>{o}</option>
          ))}
        </Select>
        <Field label="Celular com DDD" inputMode="tel" required value={phone} onChange={(e) => setPhone(maskPhone(e.target.value))} />
        <fieldset className="stack-sm">
          <legend className="small">Valor</legend>
          <div className="chips">
            {options.data.amounts.map((a) => (
              <button key={a} type="button" className={`chip ${amount === a ? 'chip-active' : ''}`} aria-pressed={amount === a} onClick={() => setAmount(a)}>
                {brl(a)}
              </button>
            ))}
          </div>
        </fieldset>
        <Button disabled={amount === null}>Recarregar</Button>
        <Alert kind="success">{recharge.data ? `Recarga de ${brl(recharge.data.amount)} feita para ${recharge.data.phone}.` : ''}</Alert>
        <p className="muted small">Operadoras fictícias: a recarga é simulada, mas o débito na conta é real (no ledger do banco).</p>
        <PinDialog open={asking} busy={recharge.isPending} error={recharge.error?.message} onConfirm={(pin) => recharge.mutate(pin)} onCancel={() => setAsking(false)} />
      </form>
    </Card>
  )
}
