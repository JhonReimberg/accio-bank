import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, ErrorBox, Field, Loading, PageHeader } from '../../components/ui'
import { api } from '../../lib/api'
import { dateTime } from '../../lib/format'
import { useAuth } from '../../lib/useAuth'
import type { CreditPolicy, ScoreBand } from '../../lib/types'

// Rates are edited as percentages (4.5) and sent as fractions (0.045).
const pct = (v: number) => Number((v * 100).toFixed(4))
const frac = (v: number) => Number((v / 100).toFixed(6))

export default function PolicyPage() {
  const { state } = useAuth()
  const isAdmin = state.status === 'authenticated' && state.session.role === 'Admin'
  const client = useQueryClient()
  const policy = useQuery({ queryKey: ['backoffice', 'policy'], queryFn: () => api<CreditPolicy>('/admin/credit/policy') })
  // Local edits on top of the server copy; null = untouched.
  const [edited, setDraft] = useState<CreditPolicy | null>(null)

  const save = useMutation({
    mutationFn: (p: CreditPolicy) => api<CreditPolicy>('/admin/credit/policy', { method: 'PUT', body: p }),
    onSuccess: (p) => {
      client.setQueryData(['backoffice', 'policy'], p)
      setDraft(null)
    },
  })

  if (policy.isPending) return <Loading />
  if (policy.isError) return <ErrorBox error={policy.error} retry={policy.refetch} />
  const draft = edited ?? policy.data

  const set = <K extends keyof CreditPolicy>(key: K, value: CreditPolicy[K]) => setDraft({ ...draft, [key]: value })
  const setBand = (i: number, band: Partial<ScoreBand>) => set('bands', draft.bands.map((b, j) => (j === i ? { ...b, ...band } : b)))
  const num = (v: string) => Number(v.replace(',', '.'))

  return (
    <div className="stack-lg">
      <PageHeader title="Política de crédito" />
      {!isAdmin && <Alert kind="info">Somente o perfil Admin pode alterar a política. Você está vendo em modo leitura.</Alert>}
      <form
        className="stack-lg"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate(draft)
        }}
      >
        <fieldset disabled={!isAdmin} className="stack-lg plain">
          <Card className="stack">
            <h2>Regras de elegibilidade</h2>
            <div className="grid-3">
              <Field label="Tempo mínimo de conta (dias)" type="number" min={0} value={draft.minAccountAgeDays} onChange={(e) => set('minAccountAgeDays', num(e.target.value))} />
              <Field label="Movimentação mínima em 30 dias (R$)" type="number" min={0} step="0.01" value={draft.minMovement30Days} onChange={(e) => set('minMovement30Days', num(e.target.value))} />
              <Field label="Score mínimo" type="number" min={0} max={1000} value={draft.minScore} onChange={(e) => set('minScore', num(e.target.value))} />
              <Field label="Comprometimento máximo da renda (%)" type="number" step="0.1" value={pct(draft.maxIncomeCommitment)} onChange={(e) => set('maxIncomeCommitment', frac(num(e.target.value)))} />
              <Field label="Parcelas (mín.)" type="number" min={1} value={draft.minInstallments} onChange={(e) => set('minInstallments', num(e.target.value))} />
              <Field label="Parcelas (máx.)" type="number" min={1} value={draft.maxInstallments} onChange={(e) => set('maxInstallments', num(e.target.value))} />
              <Field label="Valor mínimo (R$)" type="number" min={1} value={draft.minLoanAmount} onChange={(e) => set('minLoanAmount', num(e.target.value))} />
            </div>
          </Card>
          <Card className="stack">
            <h2>Encargos e taxas de referência</h2>
            <div className="grid-3">
              <Field label="Multa por atraso (%)" type="number" step="0.01" max={2} value={pct(draft.lateFeeRate)} onChange={(e) => set('lateFeeRate', frac(num(e.target.value)))} />
              <Field label="Juros de mora ao dia (%)" type="number" step="0.001" value={pct(draft.dailyLateInterestRate)} onChange={(e) => set('dailyLateInterestRate', frac(num(e.target.value)))} />
              <Field label="Cheque especial (% a.m.)" type="number" step="0.01" max={8} value={pct(draft.overdraftMonthlyRate)} onChange={(e) => set('overdraftMonthlyRate', frac(num(e.target.value)))} />
              <Field label="CDI (% a.a.)" type="number" step="0.01" value={pct(draft.cdiAnnualRate)} onChange={(e) => set('cdiAnnualRate', frac(num(e.target.value)))} />
              <Field label="Empréstimo com garantia (% a.m.)" type="number" step="0.01" value={pct(draft.guaranteedMonthlyRate)} onChange={(e) => set('guaranteedMonthlyRate', frac(num(e.target.value)))} />
            </div>
          </Card>
          <Card className="stack">
            <h2>Faixas de score</h2>
            <p className="muted small">Cada faixa vale do score mínimo até a próxima. Scores maiores devem ter taxa menor ou igual.</p>
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Score mínimo</th>
                    <th>Taxa (% a.m.)</th>
                    <th>Pré-aprovado (× renda)</th>
                    <th>Cheque especial (R$)</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  {draft.bands.map((b, i) => (
                    <tr key={i}>
                      <td><input aria-label="Score mínimo" type="number" value={b.minScore} onChange={(e) => setBand(i, { minScore: num(e.target.value) })} /></td>
                      <td><input aria-label="Taxa mensal" type="number" step="0.01" value={pct(b.monthlyRate)} onChange={(e) => setBand(i, { monthlyRate: frac(num(e.target.value)) })} /></td>
                      <td><input aria-label="Multiplicador de renda" type="number" step="0.1" value={b.preApprovedIncomeMultiplier} onChange={(e) => setBand(i, { preApprovedIncomeMultiplier: num(e.target.value) })} /></td>
                      <td><input aria-label="Limite de cheque especial" type="number" step="50" value={b.overdraftLimit} onChange={(e) => setBand(i, { overdraftLimit: num(e.target.value) })} /></td>
                      <td>
                        <Button type="button" variant="danger" onClick={() => set('bands', draft.bands.filter((_, j) => j !== i))}>
                          Remover
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Button
              type="button"
              variant="ghost"
              onClick={() => set('bands', [...draft.bands, { minScore: 900, monthlyRate: 0.02, preApprovedIncomeMultiplier: 6, overdraftLimit: 8000 }])}
            >
              Adicionar faixa
            </Button>
          </Card>
        </fieldset>
        {isAdmin && (
          <>
            <Alert kind="error">{save.error?.message}</Alert>
            <Alert kind="success">{save.isSuccess ? 'Política salva. A alteração foi registrada na auditoria.' : ''}</Alert>
            <Button busy={save.isPending}>Salvar política</Button>
          </>
        )}
        {policy.data.updatedAt && <p className="muted small">Última alteração: {dateTime(policy.data.updatedAt)}</p>}
      </form>
    </div>
  )
}
