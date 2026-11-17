import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router'
import { Alert, Button, Card, ErrorBox, Field, Loading, PageHeader, PinDialog, Select } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, dateOnly, parseAmount, percent } from '../../lib/format'
import { keys, useMoneyMoved } from '../../lib/queries'
import type { Eligibility, LoanOffer, LoanQuote, RuleResult, Score } from '../../lib/types'

export default function CreditPage() {
  const eligibility = useQuery({ queryKey: [...keys.account, 'eligibility'], queryFn: () => api<Eligibility>('/credit/eligibility') })

  return (
    <div className="stack-lg">
      <PageHeader title="Crédito">
        <Link to="/credito/emprestimos">Meus empréstimos</Link>
      </PageHeader>
      {eligibility.isPending ? (
        <Loading />
      ) : eligibility.isError ? (
        <ErrorBox error={eligibility.error} retry={eligibility.refetch} />
      ) : eligibility.data.eligible && eligibility.data.offer ? (
        <Simulator offer={eligibility.data.offer} />
      ) : (
        <Blocked rules={eligibility.data.rules} />
      )}
      <ScoreCard />
      <Card className="row-between">
        <div>
          <h2>Cheque especial</h2>
          <p className="muted small">Limite extra na conta, com juros só nos dias em que for usado.</p>
        </div>
        <Link to="/credito/cheque-especial" className="btn btn-ghost">
          Ver limite
        </Link>
      </Card>
    </div>
  )
}

/** Shows what is missing instead of a plain "no" (✅ done, ⏳ on the way, ❌ needs action). */
function Blocked({ rules }: { rules: RuleResult[] }) {
  const icon = (r: RuleResult) => (r.passed ? '✅' : r.progress !== null && r.progress > 0 ? '⏳' : '❌')
  return (
    <Card className="stack">
      <h2>O empréstimo ainda não está disponível para você</h2>
      <p className="muted">Veja o que falta. Assim que tudo estiver ✅ liberamos sua oferta.</p>
      <ul className="requirements">
        {rules.map((r) => (
          <li key={r.code} className={r.passed ? 'ok' : ''}>
            <span aria-hidden>{icon(r)}</span>
            <div className="stack-sm">
              <span>{r.message}</span>
              {!r.passed && r.progress !== null && (
                <progress max={1} value={r.progress} aria-label={`Progresso: ${Math.round(r.progress * 100)}%`} />
              )}
            </div>
          </li>
        ))}
      </ul>
    </Card>
  )
}

function Simulator({ offer }: { offer: LoanOffer }) {
  const navigate = useNavigate()
  const client = useQueryClient()
  const moneyMoved = useMoneyMoved()
  const [amount, setAmount] = useState(String(Math.min(offer.maxAmount, 1000)).replace('.', ','))
  const [installments, setInstallments] = useState(String(Math.min(12, offer.maxInstallments)))
  const [showTable, setShowTable] = useState(false)
  const [asking, setAsking] = useState(false)
  const value = parseAmount(amount)

  const simulate = useMutation({
    mutationFn: () => api<LoanQuote>('/credit/loans/simulate', { body: { amount: value, installments: Number(installments) } }),
  })
  const contract = useMutation({
    mutationFn: (pin: string) => api('/credit/loans', { body: { amount: value, installments: Number(installments), pin }, idempotent: true }),
    onSuccess: () => {
      setAsking(false)
      moneyMoved()
      client.invalidateQueries({ queryKey: [...keys.account, 'eligibility'] })
      navigate('/credito/emprestimos')
    },
  })
  const quote = simulate.data
  const options = Array.from({ length: offer.maxInstallments - offer.minInstallments + 1 }, (_, i) => offer.minInstallments + i)

  return (
    <>
      <section className="card balance">
        <span>Empréstimo pessoal pré-aprovado</span>
        <p className="balance-value">Até {brl(offer.maxAmount)}</p>
        <p className="small">Taxa de {percent(offer.monthlyRate)} ao mês · parcela de até {brl(offer.maxInstallmentAmount)}</p>
      </section>

      <Card>
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault()
            setShowTable(false)
            simulate.mutate()
          }}
        >
          <h2>Simular</h2>
          <div className="grid-2">
            <Field label="Valor (R$)" inputMode="decimal" required value={amount} onChange={(e) => (setAmount(e.target.value), simulate.reset())} />
            <Select label="Parcelas" value={installments} onChange={(e) => (setInstallments(e.target.value), simulate.reset())}>
              {options.map((n) => (
                <option key={n} value={n}>
                  {n}x
                </option>
              ))}
            </Select>
          </div>
          <Alert kind="error">{simulate.error?.message}</Alert>
          <Button variant="ghost" busy={simulate.isPending} disabled={Number.isNaN(value)}>
            Simular
          </Button>
        </form>

        {quote && (
          <div className="stack quote">
            <p className="big">
              {quote.installments}x de {brl(quote.installmentAmount)}
            </p>
            <dl className="summary">
              <dt>Você recebe</dt>
              <dd>{brl(quote.netAmount)}</dd>
              <dt>IOF</dt>
              <dd>{brl(quote.iof)}</dd>
              <dt>Juros totais</dt>
              <dd>{brl(quote.totalInterest)}</dd>
              <dt>Total a pagar</dt>
              <dd>{brl(quote.totalPaid)}</dd>
              <dt>Taxa</dt>
              <dd>{percent(quote.monthlyRate)} a.m.</dd>
              <dt>CET</dt>
              <dd>
                {percent(quote.cetMonthly)} a.m. · {percent(quote.cetYearly)} a.a.
              </dd>
              <dt>1º vencimento</dt>
              <dd>{dateOnly(quote.schedule[0].dueDate)}</dd>
            </dl>
            <Button variant="link" onClick={() => setShowTable(!showTable)}>
              {showTable ? 'Esconder' : 'Ver'} tabela de amortização
            </Button>
            {showTable && <AmortizationTable quote={quote} />}
            <Button
              onClick={() => {
                contract.reset()
                setAsking(true)
              }}
            >
              Contratar
            </Button>
          </div>
        )}
        <PinDialog
          open={asking}
          title="Confirme a contratação com sua senha transacional"
          busy={contract.isPending}
          error={contract.error?.message}
          onConfirm={(pin) => contract.mutate(pin)}
          onCancel={() => setAsking(false)}
        />
      </Card>
    </>
  )
}

function AmortizationTable({ quote }: { quote: LoanQuote }) {
  return (
    <div className="table-wrap">
      <table className="table">
        <caption className="muted small">Tabela Price: parcelas iguais, juros caindo e amortização subindo.</caption>
        <thead>
          <tr>
            <th>Nº</th>
            <th>Vencimento</th>
            <th>Parcela</th>
            <th>Juros</th>
            <th>Amortização</th>
            <th>Saldo devedor</th>
          </tr>
        </thead>
        <tbody>
          {quote.schedule.map((r) => (
            <tr key={r.number}>
              <td>{r.number}</td>
              <td>{dateOnly(r.dueDate)}</td>
              <td>{brl(r.payment)}</td>
              <td>{brl(r.interest)}</td>
              <td>{brl(r.principal)}</td>
              <td>{brl(r.balance)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function ScoreCard() {
  const score = useQuery({ queryKey: [...keys.account, 'score'], queryFn: () => api<Score>('/credit/score') })
  if (!score.data) return null
  const s = score.data
  return (
    <Card className="stack">
      <div className="row-between">
        <h2>Seu score Vela</h2>
        <p className="big">{s.score}</p>
      </div>
      <meter min={0} max={1000} low={400} high={800} optimum={1000} value={s.score} aria-label={`Score ${s.score} de 1000`} />
      <ul className="list">
        {s.factors.map((f) => (
          <li key={f.code}>
            <span>{f.description}</span>
            <span className={f.points < 0 ? 'amount-out' : 'amount-in'}>
              {f.points > 0 ? '+' : ''}
              {f.points}
            </span>
          </li>
        ))}
      </ul>
      <p className="muted small">Recalculado todos os dias. Pagar em dia e movimentar a conta aumentam seu score.</p>
    </Card>
  )
}
