import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams } from 'react-router'
import { Alert, BankCardVisual, Button, Card, ListRow, RowGroup, Empty, ErrorBox, Field, Loading, PageHeader, PinDialog, Select } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, parseAmount } from '../../lib/format'
import { keys, useMoneyMoved } from '../../lib/queries'
import type { BankCard, CardDetails, CreditLine, Purchase } from '../../lib/types'

const cardsKey = keys.cards
const creditLineKey = keys.creditLine

const formLabel: Record<BankCard['form'], string> = { Physical: 'Físico', Virtual: 'Virtual', VirtualTemporary: 'Virtual temporário' }
const statusLabel: Record<BankCard['status'], string> = { Active: 'Ativo', Blocked: 'Bloqueado', Cancelled: 'Cancelado' }

/** Cards hub: credit summary and the card list; each card opens its own screen. */
export default function CardsPage() {
  const cards = useQuery({ queryKey: cardsKey, queryFn: () => api<BankCard[]>('/cards') })
  const line = useQuery({ queryKey: creditLineKey, queryFn: async () => (await api<CreditLine | undefined>('/cards/credit-line')) ?? null })

  if (cards.isPending || line.isPending) return <Loading />
  if (cards.isError) return <ErrorBox error={cards.error} retry={cards.refetch} />
  if (line.isError) return <ErrorBox error={line.error} retry={line.refetch} />
  const active = cards.data.filter((c) => c.status !== 'Cancelled')

  return (
    <div className="stack-lg">
      <PageHeader title="Meus cartões" back="/" />
      {line.data && <CreditSummary line={line.data} />}
      {active.length === 0 ? (
        <Empty>Nenhum cartão ainda. Peça o seu abaixo.</Empty>
      ) : (
        <div className="card-strip">
          {active.map((c) => (
            <Link key={c.id} to={`/cartoes/${c.id}`} className="card-link" aria-label={`Abrir cartão final ${c.masked.slice(-4)}`}>
              <BankCardVisual
                credit={c.function === 'Credit'}
                virtual={c.form !== 'Physical'}
                blocked={c.status === 'Blocked'}
                label={`${c.function === 'Credit' ? 'Crédito' : 'Débito'} · ${formLabel[c.form]}`}
                number={`•••• •••• ${c.masked}`}
                holder={c.holderName}
                expiry={c.expiry}
              />
            </Link>
          ))}
        </div>
      )}
      <RowGroup>
        {line.data ? (
          <ListRow to="/cartoes/fatura" icon="▤" title="Fatura" text="Ver, pagar e parcelar" />
        ) : (
          <ListRow to="/cartoes/ativar-credito" icon="★" title="Ativar cartão de crédito" text="Sem anuidade" />
        )}
        <ListRow to="/cartoes/novo" icon="+" title="Pedir novo cartão" text="Físico, virtual ou temporário" />
      </RowGroup>
    </div>
  )
}

export function CardDetailPage() {
  const { id } = useParams()
  const cards = useQuery({ queryKey: cardsKey, queryFn: () => api<BankCard[]>('/cards') })
  if (cards.isPending) return <Loading />
  if (cards.isError) return <ErrorBox error={cards.error} retry={cards.refetch} />
  const card = cards.data.find((c) => c.id === id)
  return (
    <div className="stack-lg">
      <PageHeader title={card ? `Cartão final ${card.masked.slice(-4)}` : 'Cartão'} back="/cartoes" />
      {card ? <CardItem card={card} /> : <Empty>Cartão não encontrado.</Empty>}
    </div>
  )
}

export function SimulatePurchasePage() {
  const { id } = useParams()
  const cards = useQuery({ queryKey: cardsKey, queryFn: () => api<BankCard[]>('/cards') })
  const card = cards.data?.find((c) => c.id === id)
  return (
    <div className="stack-lg">
      <PageHeader title="Simular compra" back={`/cartoes/${id}`} />
      <p className="muted">Faz o papel da maquininha da loja: a resposta vem como viria da bandeira.</p>
      {cards.isPending ? <Loading /> : card ? <Card><PurchaseSimulator card={card} /></Card> : <Empty>Cartão não encontrado.</Empty>}
    </div>
  )
}

export function IssueCardPage() {
  const line = useQuery({ queryKey: creditLineKey, queryFn: async () => (await api<CreditLine | undefined>('/cards/credit-line')) ?? null })
  return (
    <div className="stack-lg">
      <PageHeader title="Pedir novo cartão" back="/cartoes" />
      {line.isPending ? <Loading /> : <IssueCard hasCreditLine={!!line.data} />}
    </div>
  )
}

export function OpenCreditLinePage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Cartão de crédito" back="/cartoes" />
      <OpenCreditLine />
    </div>
  )
}

function CreditSummary({ line }: { line: CreditLine }) {
  const current = line.invoices.find((i) => i.status === 'Open' || i.status === 'Closed')
  return (
    <Card className="stack">
      <div className="grid-2">
        <div>
          <p className="muted small">Limite disponível</p>
          <p className="big">{brl(line.available)}</p>
        </div>
        <div>
          <p className="muted small">Limite total</p>
          <p className="big">{brl(line.limit)}</p>
        </div>
      </div>
      <progress max={line.limit || 1} value={line.used} aria-label="Uso do limite" />
      {current && (
        <p className="small">
          Fatura de {current.dueDate.split('-').reverse().slice(1).join('/')}: <strong>{brl(current.remaining)}</strong> · vence dia {line.dueDay}
        </p>
      )}
    </Card>
  )
}

function OpenCreditLine() {
  const client = useQueryClient()
  const navigate = useNavigate()
  const offer = useQuery({ queryKey: [...creditLineKey, 'offer'], queryFn: () => api<{ maxLimit: number }>('/cards/credit-line/offer') })
  const [dueDay, setDueDay] = useState('10')
  const open = useMutation({
    mutationFn: () => api<CreditLine>('/cards/credit-line', { method: 'POST', body: { dueDay: Number(dueDay) } }),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: creditLineKey })
      navigate('/cartoes')
    },
  })
  if (offer.isPending) return <Loading />
  if (offer.isError) return <ErrorBox error={offer.error} retry={offer.refetch} />

  return (
    <Card className="stack">
      <h2>Cartão de crédito</h2>
      {offer.data.maxLimit <= 0 ? (
        <p className="muted">No momento não há limite de crédito disponível para você. Ele depende do seu score.</p>
      ) : (
        <>
          <p>
            Limite aprovado de <strong>{brl(offer.data.maxLimit)}</strong>, sem anuidade.
          </p>
          <Select label="Dia de vencimento" value={dueDay} onChange={(e) => setDueDay(e.target.value)}>
            {[1, 5, 10, 15, 20, 25].map((d) => (
              <option key={d} value={d}>
                Dia {d}
              </option>
            ))}
          </Select>
          <p className="muted small">A fatura fecha 7 dias antes do vencimento.</p>
          <Alert kind="error">{open.error?.message}</Alert>
          <Button busy={open.isPending} onClick={() => open.mutate()}>
            Ativar cartão de crédito
          </Button>
        </>
      )}
    </Card>
  )
}

function CardItem({ card }: { card: BankCard }) {
  const client = useQueryClient()
  const [details, setDetails] = useState<CardDetails | null>(null)
  const [revealing, setRevealing] = useState(false)
  const update = useMutation({
    mutationFn: (body: { onlinePurchasesEnabled?: boolean; blocked?: boolean }) => api(`/cards/${card.id}`, { method: 'PATCH', body }),
    onSuccess: () => client.invalidateQueries({ queryKey: cardsKey }),
  })
  const reveal = useMutation({
    mutationFn: (pin: string) => api<CardDetails>(`/cards/${card.id}/reveal`, { method: 'POST', body: { pin } }),
    onSuccess: (data) => {
      setRevealing(false)
      setDetails(data)
    },
  })
  const blocked = card.status === 'Blocked'

  return (
    <Card className="stack">
      <BankCardVisual
        credit={card.function === 'Credit'}
        virtual={card.form !== 'Physical'}
        blocked={blocked}
        label={`${card.function === 'Credit' ? 'Crédito' : 'Débito'} · ${formLabel[card.form]}`}
        number={details ? details.number.replace(/(\d{4})(?=\d)/g, '$1 ') : `•••• •••• ${card.masked}`}
        holder={card.holderName}
        expiry={card.expiry}
      />
      <div className="row-between small">
        <span className="badge-soft">{statusLabel[card.status]}</span>
        <label className="row">
          <input
            type="checkbox"
            checked={card.onlinePurchasesEnabled}
            disabled={update.isPending}
            onChange={(e) => update.mutate({ onlinePurchasesEnabled: e.target.checked })}
          />
          Compras online
        </label>
      </div>
      {card.form === 'VirtualTemporary' && <p className="muted small">Vale para uma única compra e depois é cancelado sozinho.</p>}
      <div className="row">
        <Button variant="ghost" busy={update.isPending} onClick={() => update.mutate({ blocked: !blocked })}>
          {blocked ? 'Desbloquear' : 'Bloquear'}
        </Button>
        {card.form !== 'Physical' && !details && (
          <Button variant="ghost" onClick={() => setRevealing(true)}>
            Ver dados
          </Button>
        )}
        <Link to={`/cartoes/${card.id}/simular`} className="btn btn-ghost">
          Simular compra
        </Link>
      </div>
      <Alert kind="error">{update.error?.message}</Alert>
      <PinDialog
        open={revealing}
        title="Confirme para ver os dados do cartão"
        busy={reveal.isPending}
        error={reveal.error?.message}
        onConfirm={(pin) => reveal.mutate(pin)}
        onCancel={() => setRevealing(false)}
      />
    </Card>
  )
}

/** Stands in for a shop's card machine or checkout: the API answers like a card network would. */
function PurchaseSimulator({ card }: { card: BankCard }) {
  const client = useQueryClient()
  const moneyMoved = useMoneyMoved()
  const [merchant, setMerchant] = useState('Mercado Central')
  const [amount, setAmount] = useState('')
  const [installments, setInstallments] = useState('1')
  const [online, setOnline] = useState(card.form !== 'Physical')
  const buy = useMutation({
    mutationFn: () =>
      api<Purchase>(`/cards/${card.id}/purchases`, {
        method: 'POST',
        body: { merchant, amount: parseAmount(amount), installments: Number(installments), online },
        idempotent: true,
      }),
    onSuccess: () => {
      moneyMoved()
      for (const key of [cardsKey, creditLineKey, [...keys.account, 'investments']]) client.invalidateQueries({ queryKey: key })
    },
  })
  const result = buy.data

  return (
    <form
      className="stack"
      onSubmit={(e) => {
        e.preventDefault()
        buy.mutate()
      }}
    >
      <Field label="Estabelecimento" required maxLength={100} value={merchant} onChange={(e) => setMerchant(e.target.value)} />
      <Field label="Valor (R$)" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} />
      {card.function === 'Credit' && (
        <Select label="Parcelas" value={installments} onChange={(e) => setInstallments(e.target.value)}>
          {Array.from({ length: 12 }, (_, k) => k + 1).map((n) => (
            <option key={n} value={n}>
              {n === 1 ? 'À vista' : `${n}x de ${Number.isNaN(parseAmount(amount)) ? '—' : brl(parseAmount(amount) / n)}`}
            </option>
          ))}
        </Select>
      )}
      <label className="row small">
        <input type="checkbox" checked={online} onChange={(e) => setOnline(e.target.checked)} />
        Compra online
      </label>
      <Button busy={buy.isPending} disabled={Number.isNaN(parseAmount(amount))}>
        Passar o cartão
      </Button>
      <Alert kind="error">{buy.error?.message}</Alert>
      {result &&
        (result.approved ? (
          <Alert kind="success">
            Compra aprovada: {brl(result.amount)}
            {result.installments > 1 ? ` em ${result.installments}x` : ''}.{result.roundUp > 0 ? ` ${brl(result.roundUp)} foram para a sua caixinha.` : ''}
          </Alert>
        ) : (
          <Alert kind="error">Compra recusada: {result.declineReason}</Alert>
        ))}
    </form>
  )
}

function IssueCard({ hasCreditLine }: { hasCreditLine: boolean }) {
  const client = useQueryClient()
  const navigate = useNavigate()
  const [fn, setFn] = useState<BankCard['function']>('Debit')
  const [form, setForm] = useState<BankCard['form']>('Virtual')
  const issue = useMutation({
    mutationFn: () => api<BankCard>('/cards', { method: 'POST', body: { function: fn, form } }),
    onSuccess: (card) => {
      client.invalidateQueries({ queryKey: cardsKey })
      navigate(`/cartoes/${card.id}`)
    },
  })

  return (
    <Card>
      <form
        className="stack"
        onSubmit={(e) => {
          e.preventDefault()
          issue.mutate()
        }}
      >
        <h2>Novo cartão</h2>
        <Select
          label="Função"
          value={fn}
          onChange={(e) => {
            const value = e.target.value as BankCard['function']
            setFn(value)
            if (value === 'Debit' && form === 'VirtualTemporary') setForm('Virtual')
          }}
        >
          <option value="Debit">Débito</option>
          <option value="Credit" disabled={!hasCreditLine}>
            Crédito{hasCreditLine ? '' : ' (ative o crédito primeiro)'}
          </option>
        </Select>
        <Select label="Tipo" value={form} onChange={(e) => setForm(e.target.value as BankCard['form'])}>
          <option value="Virtual">Virtual</option>
          <option value="Physical">Físico</option>
          {fn === 'Credit' && <option value="VirtualTemporary">Virtual temporário (uma compra)</option>}
        </Select>
        <Alert kind="error">{issue.error?.message}</Alert>
        <Button busy={issue.isPending}>Emitir</Button>
      </form>
    </Card>
  )
}
