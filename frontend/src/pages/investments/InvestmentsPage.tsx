import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Empty, ErrorBox, Field, Loading, PageHeader, PinDialog, Select } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, parseAmount, percent } from '../../lib/format'
import { keys, useMoneyMoved } from '../../lib/queries'
import type { Investment, RedemptionPreview } from '../../lib/types'

const investmentsKey = [...keys.account, 'investments']

export default function InvestmentsPage() {
  const list = useQuery({ queryKey: investmentsKey, queryFn: () => api<Investment[]>('/investments') })

  if (list.isPending) return <Loading />
  if (list.isError) return <ErrorBox error={list.error} retry={list.refetch} />
  const total = list.data.reduce((sum, i) => sum + i.value, 0)

  return (
    <div className="stack-lg">
      <PageHeader title="Investimentos" />
      <Card>
        <p className="muted small">Total investido</p>
        <p className="big">{brl(total)}</p>
        <p className="muted small">Rendimento diário em dias úteis, sobre o CDI. Resgate quando quiser: IR e IOF são calculados na hora.</p>
      </Card>
      {list.data.length === 0 ? <Empty>Você ainda não tem investimentos. Abra um CDB ou uma caixinha abaixo.</Empty> : list.data.map((i) => <InvestmentCard key={i.id} investment={i} />)}
      <OpenInvestment />
    </div>
  )
}

function InvestmentCard({ investment: i }: { investment: Investment }) {
  const client = useQueryClient()
  const moneyMoved = useMoneyMoved()
  const [mode, setMode] = useState<'apply' | 'redeem' | 'loan' | null>(null)
  const roundUp = useMutation({
    mutationFn: (enabled: boolean) => api(`/investments/${i.id}/round-up`, { method: 'PUT', body: { enabled } }),
    onSuccess: () => client.invalidateQueries({ queryKey: investmentsKey }),
  })

  return (
    <Card className="stack">
      <div className="row-between">
        <div>
          <h2>{i.name}</h2>
          <p className="muted small">
            {i.type === 'Cdb' ? 'CDB' : 'Caixinha'} · {percent(i.cdiPercent)} do CDI
          </p>
        </div>
        <div className="text-right">
          <p className="big">{brl(i.value)}</p>
          <p className="small amount-in">+{brl(i.income)} de rendimento</p>
        </div>
      </div>
      {i.goal !== null && i.goalProgress !== null && (
        <div className="stack-sm">
          <progress max={1} value={Math.min(i.goalProgress, 1)} aria-label="Progresso da meta" />
          <p className="muted small">
            {percent(Math.min(i.goalProgress, 1))} da meta de {brl(i.goal)}
          </p>
        </div>
      )}
      {i.blockedAmount > 0 && <p className="muted small">{brl(i.blockedAmount)} em garantia de empréstimo.</p>}
      {i.type === 'Caixinha' && (
        <label className="row small">
          <input type="checkbox" checked={i.roundUpCardPurchases} disabled={roundUp.isPending} onChange={(e) => roundUp.mutate(e.target.checked)} />
          Arredondar compras no cartão para esta caixinha
        </label>
      )}
      <div className="row">
        <Button variant="ghost" onClick={() => setMode(mode === 'apply' ? null : 'apply')}>
          Aplicar
        </Button>
        <Button variant="ghost" onClick={() => setMode(mode === 'redeem' ? null : 'redeem')} disabled={i.availableToRedeem <= 0}>
          Resgatar
        </Button>
        {i.type === 'Cdb' && (
          <Button variant="ghost" onClick={() => setMode(mode === 'loan' ? null : 'loan')} disabled={i.availableToRedeem <= 0}>
            Empréstimo com garantia
          </Button>
        )}
      </div>
      {mode === 'apply' && <Move investment={i} kind="applications" onDone={() => { setMode(null); moneyMoved(); client.invalidateQueries({ queryKey: investmentsKey }) }} />}
      {mode === 'redeem' && <Move investment={i} kind="redemptions" onDone={() => { setMode(null); moneyMoved(); client.invalidateQueries({ queryKey: investmentsKey }) }} />}
      {mode === 'loan' && <GuaranteedLoan investment={i} onDone={() => { setMode(null); moneyMoved(); client.invalidateQueries({ queryKey: investmentsKey }) }} />}
    </Card>
  )
}

function Move({ investment, kind, onDone }: { investment: Investment; kind: 'applications' | 'redemptions'; onDone: () => void }) {
  const [amount, setAmount] = useState('')
  const [asking, setAsking] = useState(false)
  const value = parseAmount(amount)
  const valid = !Number.isNaN(value) && value > 0
  const preview = useQuery({
    queryKey: [...investmentsKey, investment.id, 'preview', value],
    queryFn: () => api<RedemptionPreview>(`/investments/${investment.id}/redemptions/preview?amount=${value}`),
    enabled: kind === 'redemptions' && valid,
    retry: false,
  })
  const move = useMutation({
    mutationFn: (pin: string) => api(`/investments/${investment.id}/${kind}`, { method: 'POST', body: { amount: value, pin }, idempotent: true }),
    onSuccess: () => {
      setAsking(false)
      onDone()
    },
  })

  return (
    <form
      className="stack"
      onSubmit={(e) => {
        e.preventDefault()
        move.reset()
        setAsking(true)
      }}
    >
      <Field
        label={kind === 'applications' ? 'Quanto aplicar (R$)' : `Quanto resgatar (até ${brl(investment.availableToRedeem)})`}
        inputMode="decimal"
        required
        value={amount}
        onChange={(e) => setAmount(e.target.value)}
      />
      {kind === 'redemptions' && preview.data && (
        <dl className="summary small">
          <dt>Bruto</dt>
          <dd>{brl(preview.data.gross)}</dd>
          <dt>Rendimento</dt>
          <dd>{brl(preview.data.income)}</dd>
          <dt>IOF</dt>
          <dd>-{brl(preview.data.iof)}</dd>
          <dt>IR</dt>
          <dd>-{brl(preview.data.incomeTax)}</dd>
          <dt>Você recebe</dt>
          <dd>
            <strong>{brl(preview.data.net)}</strong>
          </dd>
        </dl>
      )}
      {preview.isError && <Alert kind="error">{preview.error.message}</Alert>}
      <Button disabled={!valid}>{kind === 'applications' ? 'Aplicar' : 'Resgatar'}</Button>
      <PinDialog open={asking} busy={move.isPending} error={move.error?.message} onConfirm={(pin) => move.mutate(pin)} onCancel={() => setAsking(false)} />
    </form>
  )
}

function GuaranteedLoan({ investment, onDone }: { investment: Investment; onDone: () => void }) {
  const [amount, setAmount] = useState('')
  const [installments, setInstallments] = useState('12')
  const [asking, setAsking] = useState(false)
  const value = parseAmount(amount)
  const contract = useMutation({
    mutationFn: (pin: string) =>
      api('/credit/guaranteed-loans', { method: 'POST', body: { investmentId: investment.id, amount: value, installments: Number(installments), pin }, idempotent: true }),
    onSuccess: () => {
      setAsking(false)
      onDone()
    },
  })

  return (
    <form
      className="stack"
      onSubmit={(e) => {
        e.preventDefault()
        contract.reset()
        setAsking(true)
      }}
    >
      <p className="muted small">
        O valor fica bloqueado no investimento (continua rendendo) e a taxa é menor que a do empréstimo pessoal. Disponível como garantia:{' '}
        {brl(investment.availableToRedeem)}.
      </p>
      <Field label="Valor do empréstimo (R$)" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} />
      <Select label="Parcelas" value={installments} onChange={(e) => setInstallments(e.target.value)}>
        {[6, 12, 18, 24].map((n) => (
          <option key={n} value={n}>
            {n}x
          </option>
        ))}
      </Select>
      <Button disabled={Number.isNaN(value) || value <= 0}>Contratar</Button>
      <PinDialog open={asking} busy={contract.isPending} error={contract.error?.message} onConfirm={(pin) => contract.mutate(pin)} onCancel={() => setAsking(false)} />
    </form>
  )
}

function OpenInvestment() {
  const client = useQueryClient()
  const [type, setType] = useState<'Cdb' | 'Caixinha'>('Caixinha')
  const [name, setName] = useState('')
  const [goal, setGoal] = useState('')
  const open = useMutation({
    mutationFn: () => api('/investments', { method: 'POST', body: { type, name, goal: goal ? parseAmount(goal) : null } }),
    onSuccess: () => {
      setName('')
      setGoal('')
      client.invalidateQueries({ queryKey: investmentsKey })
    },
  })

  return (
    <Card>
      <form
        className="stack"
        onSubmit={(e) => {
          e.preventDefault()
          open.mutate()
        }}
      >
        <h2>Novo investimento</h2>
        <Select label="Tipo" value={type} onChange={(e) => setType(e.target.value as 'Cdb' | 'Caixinha')}>
          <option value="Caixinha">Caixinha (com meta, liquidez diária)</option>
          <option value="Cdb">CDB (liquidez diária)</option>
        </Select>
        <Field label="Nome" required maxLength={60} value={name} onChange={(e) => setName(e.target.value)} placeholder={type === 'Caixinha' ? 'Viagem, reserva…' : 'Meu CDB'} />
        {type === 'Caixinha' && <Field label="Meta (R$, opcional)" inputMode="decimal" value={goal} onChange={(e) => setGoal(e.target.value)} />}
        <Alert kind="error">{open.error?.message}</Alert>
        <Button busy={open.isPending}>Criar</Button>
      </form>
    </Card>
  )
}
