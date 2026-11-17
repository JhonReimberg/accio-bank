import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, CircleActions, Empty, ErrorBox, Field, ListRow, Loading, PageHeader, PinDialog, RowGroup, Select } from '../../components/ui'
import { useHidden } from '../../lib/hideBalance'
import { api } from '../../lib/api'
import { brl, parseAmount, percent } from '../../lib/format'
import { keys, useMoneyMoved } from '../../lib/queries'
import type { Investment, RedemptionPreview } from '../../lib/types'

const investmentsKey = [...keys.account, 'investments']

const useInvestments = () => useQuery({ queryKey: investmentsKey, queryFn: () => api<Investment[]>('/investments') })

/** Investments hub: total, one card per product; each opens its own screen. */
export default function InvestmentsPage() {
  const list = useInvestments()
  const hidden = useHidden()
  if (list.isPending) return <Loading />
  if (list.isError) return <ErrorBox error={list.error} retry={list.refetch} />
  const total = list.data.reduce((sum, i) => sum + i.value, 0)
  const income = list.data.reduce((sum, i) => sum + i.income, 0)
  const money = (v: number) => (hidden ? 'R$ ••••' : brl(v))

  return (
    <div className="stack-lg">
      <div className="invest-head">
        <div>
          <p className="muted">Total investido</p>
          <p className="hero-value">{money(total)}</p>
          <p className="amount-in small">▲ {money(income)} de rendimento</p>
        </div>
        <Link to="/investimentos/novo" className="btn btn-primary">
          Investir
        </Link>
      </div>

      {list.data.length === 0 ? (
        <Empty>Você ainda não tem investimentos. Toque em Investir para começar.</Empty>
      ) : (
        <div className="product-strip">
          {list.data.map((i) => (
            <Link key={i.id} to={`/investimentos/${i.id}`} className={`product ${i.type === 'Cdb' ? 'product-cdb' : ''}`}>
              <span className="muted small">
                {i.type === 'Cdb' ? 'CDB' : 'Cofre'} · {i.name}
              </span>
              <strong>{money(i.value)}</strong>
              <span className="amount-in small">▲ {money(i.income)}</span>
              <span className="muted small">{percent(i.cdiPercent)} do CDI</span>
            </Link>
          ))}
        </div>
      )}

      <CircleActions
        label="Investimentos"
        items={[
          { to: '/investimentos/novo', icon: '+', label: 'Novo investimento' },
          { to: '/conta?aba=guardar', icon: '◫', label: 'Cofres' },
          { to: '/investimentos/novo?tipo=Cdb', icon: '▮', label: 'CDB' },
          { to: '/credito', icon: '◔', label: 'Empréstimos' },
        ]}
      />

      <RowGroup title="Outros produtos">
        <ListRow to="/beneficios" icon="✦" title="Tesouro e ações" text="Em breve" />
      </RowGroup>
    </div>
  )
}

/** One product: values, goal, round-up, and the operations as separate screens. */
export function InvestmentDetailPage() {
  const { id } = useParams()
  const client = useQueryClient()
  const list = useInvestments()
  const hidden = useHidden()
  const roundUp = useMutation({
    mutationFn: (enabled: boolean) => api(`/investments/${id}/round-up`, { method: 'PUT', body: { enabled } }),
    onSuccess: () => client.invalidateQueries({ queryKey: investmentsKey }),
  })
  if (list.isPending) return <Loading />
  if (list.isError) return <ErrorBox error={list.error} retry={list.refetch} />
  const i = list.data.find((x) => x.id === id)
  if (!i) return <Empty>Investimento não encontrado.</Empty>
  const money = (v: number) => (hidden ? 'R$ ••••' : brl(v))

  return (
    <div className="stack-lg">
      <PageHeader title={i.name} back="/investimentos" />
      <div className="center-value">
        <p className="muted">
          {i.type === 'Cdb' ? 'CDB' : 'Cofre'} · {percent(i.cdiPercent)} do CDI
        </p>
        <p className="hero-value">{money(i.value)}</p>
        <p className="amount-in small">▲ {money(i.income)} de rendimento</p>
      </div>
      {i.goal !== null && i.goalProgress !== null && (
        <Card className="stack-sm">
          <progress max={1} value={Math.min(i.goalProgress, 1)} aria-label="Progresso da meta" />
          <p className="muted small">
            {percent(Math.min(i.goalProgress, 1))} da meta de {brl(i.goal)}
          </p>
        </Card>
      )}
      {i.blockedAmount > 0 && <p className="muted small">{brl(i.blockedAmount)} em garantia de empréstimo.</p>}
      <CircleActions
        label="Operações"
        items={[
          { to: `/investimentos/${i.id}/aplicar`, icon: '+', label: 'Aplicar' },
          ...(i.availableToRedeem > 0 ? [{ to: `/investimentos/${i.id}/resgatar`, icon: '↓', label: 'Resgatar' }] : []),
          ...(i.type === 'Cdb' && i.availableToRedeem > 0 ? [{ to: `/investimentos/${i.id}/garantia`, icon: '◔', label: 'Empréstimo com garantia' }] : []),
        ]}
      />
      {i.type === 'Caixinha' && (
        <label className="switch-row">
          <span>
            <strong>Arredondar compras</strong>
            <span className="muted small">O troco das compras no cartão vem para este cofre.</span>
          </span>
          <input type="checkbox" role="switch" checked={i.roundUpCardPurchases} disabled={roundUp.isPending} onChange={(e) => roundUp.mutate(e.target.checked)} />
        </label>
      )}
      <Alert kind="error">{roundUp.error?.message}</Alert>
    </div>
  )
}

/** Apply / redeem / guaranteed loan, each on its own screen; back to the product when done. */
export function InvestmentOperationPage({ kind }: { kind: 'applications' | 'redemptions' | 'loan' }) {
  const { id } = useParams()
  const navigate = useNavigate()
  const client = useQueryClient()
  const moneyMoved = useMoneyMoved()
  const list = useInvestments()
  const i = list.data?.find((x) => x.id === id)
  const title = kind === 'applications' ? 'Aplicar' : kind === 'redemptions' ? 'Resgatar' : 'Empréstimo com garantia'
  const done = () => {
    moneyMoved()
    client.invalidateQueries({ queryKey: investmentsKey })
    navigate(`/investimentos/${id}`)
  }
  return (
    <div className="stack-lg">
      <PageHeader title={title} back={`/investimentos/${id}`} />
      {list.isPending ? (
        <Loading />
      ) : !i ? (
        <Empty>Investimento não encontrado.</Empty>
      ) : (
        <Card>
          <p className="muted small">{i.name}</p>
          {kind === 'loan' ? <GuaranteedLoan investment={i} onDone={done} /> : <Move investment={i} kind={kind} onDone={done} />}
        </Card>
      )}
    </div>
  )
}

export function NewInvestmentPage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Novo investimento" back="/investimentos" />
      <OpenInvestment />
    </div>
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
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const [type, setType] = useState<'Cdb' | 'Caixinha'>(params.get('tipo') === 'Cdb' ? 'Cdb' : 'Caixinha')
  const [name, setName] = useState('')
  const [goal, setGoal] = useState('')
  const open = useMutation({
    mutationFn: () => api<Investment | undefined>('/investments', { method: 'POST', body: { type, name, goal: goal ? parseAmount(goal) : null } }),
    onSuccess: (created) => {
      client.invalidateQueries({ queryKey: investmentsKey })
      navigate(created?.id ? `/investimentos/${created.id}` : '/investimentos')
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
        <Select label="Tipo" value={type} onChange={(e) => setType(e.target.value as 'Cdb' | 'Caixinha')}>
          <option value="Caixinha">Cofre (com meta, liquidez diária)</option>
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
