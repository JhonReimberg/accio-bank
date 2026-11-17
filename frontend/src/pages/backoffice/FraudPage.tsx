import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Empty, ErrorBox, Field, Loading, PageHeader } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, dateTime } from '../../lib/format'
import { useAuth } from '../../lib/useAuth'
import type { FraudPolicy, FraudReview, Paged } from '../../lib/types'

const queueKey = ['backoffice', 'fraud']

export default function FraudPage() {
  const { state } = useAuth()
  const isAdmin = state.status === 'authenticated' && state.session.role === 'Admin'
  const client = useQueryClient()
  const queue = useQuery({ queryKey: queueKey, queryFn: () => api<Paged<FraudReview>>('/admin/fraud/reviews') })
  const decide = useMutation({
    mutationFn: ({ id, approve }: { id: string; approve: boolean }) => api(`/admin/fraud/reviews/${id}/${approve ? 'approve' : 'reject'}`, { method: 'POST' }),
    onSettled: () => client.invalidateQueries({ queryKey: queueKey }),
  })

  return (
    <div className="stack-lg">
      <PageHeader title="Antifraude" />
      <Card className="stack">
        <h2>PIX retidos para análise</h2>
        <p className="muted small">Aprovar envia o PIX na hora; recusar devolve o valor reservado ao cliente. Os dois avisam o cliente.</p>
        <Alert kind="error">{decide.error?.message}</Alert>
        {queue.isPending ? (
          <Loading />
        ) : queue.isError ? (
          <ErrorBox error={queue.error} retry={queue.refetch} />
        ) : queue.data.items.length === 0 ? (
          <Empty>Nenhuma transação aguardando análise.</Empty>
        ) : (
          <ul className="list">
            {queue.data.items.map((r) => (
              <li key={r.id} className="stack-sm">
                <div className="row-between">
                  <span>
                    <strong>{brl(r.amount)}</strong> para {r.payeeName}
                  </span>
                  <span className="muted small">{dateTime(r.createdAt)}</span>
                </div>
                <p className="small amount-out">{r.reasons}</p>
                {r.description && <p className="muted small">“{r.description}”</p>}
                <div className="row">
                  <Button variant="ghost" busy={decide.isPending} onClick={() => decide.mutate({ id: r.id, approve: false })}>
                    Recusar
                  </Button>
                  <Button busy={decide.isPending} onClick={() => decide.mutate({ id: r.id, approve: true })}>
                    Aprovar
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </Card>
      <Rules editable={isAdmin} />
    </div>
  )
}

function Rules({ editable }: { editable: boolean }) {
  const client = useQueryClient()
  const policy = useQuery({ queryKey: [...queueKey, 'policy'], queryFn: () => api<FraudPolicy>('/admin/fraud/policy') })
  const [draft, setDraft] = useState<FraudPolicy | null>(null)
  const save = useMutation({
    mutationFn: (body: FraudPolicy) => api<FraudPolicy>('/admin/fraud/policy', { method: 'PUT', body }),
    onSuccess: (data) => {
      client.setQueryData([...queueKey, 'policy'], data)
      setDraft(null)
    },
  })
  if (policy.isPending) return <Loading />
  if (policy.isError) return <ErrorBox error={policy.error} retry={policy.refetch} />
  const p = draft ?? policy.data
  const set = (field: keyof FraudPolicy, value: number | boolean) => setDraft({ ...p, [field]: value })

  return (
    <Card>
      <form
        className="stack"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate(p)
        }}
      >
        <h2>Regras</h2>
        <fieldset disabled={!editable} className="stack">
          <label className="row">
            <input type="checkbox" checked={p.enabled} onChange={(e) => set('enabled', e.target.checked)} />
            Antifraude ligado
          </label>
          <div className="grid-2">
            <Field label="Valor acima de N× a média do cliente" type="number" min={1} step="0.5" value={p.averageMultiplier} onChange={(e) => set('averageMultiplier', Number(e.target.value))} />
            <Field label="…e a partir de (R$)" type="number" min={0} value={p.averageRuleMinAmount} onChange={(e) => set('averageRuleMinAmount', Number(e.target.value))} />
            <Field label="Dispositivo novo a partir de (R$)" type="number" min={0} value={p.newDeviceAmount} onChange={(e) => set('newDeviceAmount', Number(e.target.value))} />
            <Field label="Pagamentos em 10 minutos" type="number" min={2} value={p.velocityCount} onChange={(e) => set('velocityCount', Number(e.target.value))} />
          </div>
        </fieldset>
        {editable ? (
          <Button busy={save.isPending} disabled={!draft}>
            Salvar regras
          </Button>
        ) : (
          <p className="muted small">Só o Admin altera as regras.</p>
        )}
        <Alert kind="error">{save.error?.message}</Alert>
      </form>
    </Card>
  )
}
