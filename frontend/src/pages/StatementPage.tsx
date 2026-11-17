import { useState, type SyntheticEvent } from 'react'
import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router'
import { Alert, Amount, Button, Card, Empty, ErrorBox, Field, Loading, PageHeader, Pagination, PinDialog, Select } from '../components/ui'
import { api, download } from '../lib/api'
import { brl, dateTime, kindLabel, parseAmount } from '../lib/format'
import { keys, useMoneyMoved } from '../lib/queries'
import type { Paged, PixTransferDetail, StatementItem } from '../lib/types'

export default function StatementPage() {
  // Filters live in the URL: shareable, and kept when coming back from a detail.
  const [params, setParams] = useSearchParams()
  const page = Number(params.get('page') ?? 1)
  const query = new URLSearchParams(params)
  query.set('pageSize', '15')
  const statement = useQuery({
    queryKey: [...keys.statement, query.toString()],
    queryFn: () => api<Paged<StatementItem>>(`/account/statement?${query}`),
    placeholderData: keepPreviousData,
  })
  const [selected, setSelected] = useState<StatementItem | null>(null)

  function applyFilters(e: SyntheticEvent<HTMLFormElement>) {
    e.preventDefault()
    const next = new URLSearchParams()
    for (const [k, v] of new FormData(e.currentTarget)) if (v) next.set(k, String(v))
    setParams(next)
  }

  return (
    <div className="stack-lg">
      <PageHeader title="Extrato" back="/conta" />
      <details className="card filters-box" open={[...params.keys()].some((k) => k !== 'page')}>
        <summary>Filtrar por data, tipo ou nome</summary>
        <form className="filters" onSubmit={applyFilters}>
          <Field label="De" name="from" type="date" defaultValue={params.get('from') ?? ''} />
          <Field label="Até" name="to" type="date" defaultValue={params.get('to') ?? ''} />
          <Select label="Tipo" name="kind" defaultValue={params.get('kind') ?? ''}>
            <option value="">Todos</option>
            {Object.entries(kindLabel).map(([value, label]) => (
              <option key={value} value={value}>
                {label}
              </option>
            ))}
          </Select>
          <Select label="Sentido" name="direction" defaultValue={params.get('direction') ?? ''}>
            <option value="">Entradas e saídas</option>
            <option value="In">Entradas</option>
            <option value="Out">Saídas</option>
          </Select>
          <Field label="Buscar" name="search" defaultValue={params.get('search') ?? ''} placeholder="Nome ou descrição" />
          <Button variant="ghost">Filtrar</Button>
        </form>
      </details>

      <Card>
        {statement.isPending ? (
          <Loading />
        ) : statement.isError ? (
          <ErrorBox error={statement.error} retry={statement.refetch} />
        ) : statement.data.items.length === 0 ? (
          <Empty>Nenhuma movimentação encontrada.</Empty>
        ) : (
          <>
            <ul className="list list-clickable">
              {statement.data.items.map((t) => (
                <li key={t.transactionId + t.direction}>
                  <button onClick={() => setSelected(t)}>
                    <div>
                      <p>{t.description}</p>
                      <p className="muted small">
                        {kindLabel[t.kind]} · {dateTime(t.createdAt)}
                      </p>
                    </div>
                    <Amount value={t.amount} />
                  </button>
                </li>
              ))}
            </ul>
            <Pagination
              page={page}
              totalPages={statement.data.totalPages}
              onChange={(p) => {
                const next = new URLSearchParams(params)
                next.set('page', String(p))
                setParams(next)
              }}
            />
          </>
        )}
      </Card>

      {selected && (
        <div className="sheet-backdrop" onClick={() => setSelected(null)}>
          <div className="sheet" role="dialog" aria-modal="true" aria-label="Detalhe da transação" onClick={(e) => e.stopPropagation()}>
            <TransactionDetail item={selected} onClose={() => setSelected(null)} />
          </div>
        </div>
      )}
    </div>
  )
}

function TransactionDetail({ item, onClose }: { item: StatementItem; onClose: () => void }) {
  const isPix = item.kind === 'Pix'
  const pix = useQuery({
    queryKey: [...keys.pix, 'by-transaction', item.transactionId],
    queryFn: () => api<PixTransferDetail>(`/pix/transfers/by-transaction/${item.transactionId}`),
    enabled: isPix,
  })
  const [receiptError, setReceiptError] = useState('')

  return (
    <Card className="detail stack">
      <div className="row-between">
        <h2>{kindLabel[item.kind]}</h2>
        <Button variant="ghost" onClick={onClose}>
          Fechar
        </Button>
      </div>
      <p className="detail-amount">
        <Amount value={item.amount} />
      </p>
      <dl className="summary">
        <dt>Descrição</dt>
        <dd>{item.description}</dd>
        {item.counterparty && (
          <>
            <dt>{item.amount < 0 ? 'Para' : 'De'}</dt>
            <dd>{item.counterparty}</dd>
          </>
        )}
        <dt>Data</dt>
        <dd>{dateTime(item.createdAt)}</dd>
        <dt>Saldo após</dt>
        <dd>{brl(item.balanceAfter)}</dd>
        {pix.data && (
          <>
            <dt>ID PIX</dt>
            <dd className="mono">{pix.data.endToEndId}</dd>
            {pix.data.refundedAmount > 0 && (
              <>
                <dt>Devolvido</dt>
                <dd>{brl(pix.data.refundedAmount)}</dd>
              </>
            )}
          </>
        )}
      </dl>
      <Button
        variant="ghost"
        onClick={() =>
          download(`/account/statement/${item.transactionId}/receipt`, `comprovante-${item.transactionId}.pdf`).catch((e: Error) =>
            setReceiptError(e.message),
          )
        }
      >
        Baixar comprovante (PDF)
      </Button>
      <Alert kind="error">{receiptError}</Alert>
      {pix.data?.canRefund && <RefundForm pix={pix.data} />}
    </Card>
  )
}

function RefundForm({ pix }: { pix: PixTransferDetail }) {
  const moneyMoved = useMoneyMoved()
  const [amount, setAmount] = useState(String(pix.refundableAmount).replace('.', ','))
  const [reason, setReason] = useState('')
  const [asking, setAsking] = useState(false)
  const [done, setDone] = useState('')
  const refund = useMutation({
    mutationFn: (pin: string) =>
      api(`/pix/transfers/${pix.id}/refunds`, { body: { amount: parseAmount(amount), reason, pin }, idempotent: true }),
    onSuccess: () => {
      setAsking(false)
      setDone('Devolução enviada.')
      moneyMoved()
    },
  })

  return (
    <form
      className="stack refund"
      onSubmit={(e) => {
        e.preventDefault()
        refund.reset()
        setAsking(true)
      }}
    >
      <h3>Devolver este PIX</h3>
      <p className="muted small">Recebeu por engano ou a mais? Devolva tudo ou parte (até {brl(pix.refundableAmount)}).</p>
      <div className="grid-2">
        <Field label="Valor (R$)" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} />
        <Field label="Motivo" required maxLength={140} value={reason} onChange={(e) => setReason(e.target.value)} />
      </div>
      <Alert kind="success">{done}</Alert>
      <Button variant="ghost">Devolver</Button>
      <PinDialog
        open={asking}
        busy={refund.isPending}
        error={refund.error?.message}
        onConfirm={(pin) => refund.mutate(pin)}
        onCancel={() => setAsking(false)}
      />
    </form>
  )
}
