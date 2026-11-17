import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, ErrorBox, Field, Loading, PageHeader, PinDialog } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, parseAmount, percent } from '../../lib/format'
import { keys } from '../../lib/queries'
import type { Overdraft } from '../../lib/types'

export default function OverdraftPage() {
  const client = useQueryClient()
  const overdraft = useQuery({ queryKey: [...keys.account, 'overdraft'], queryFn: () => api<Overdraft>('/credit/overdraft') })
  const [limit, setLimit] = useState('')
  const [asking, setAsking] = useState(false)
  const save = useMutation({
    mutationFn: (pin: string) => api<Overdraft>('/credit/overdraft', { method: 'PUT', body: { limit: parseAmount(limit), pin } }),
    onSuccess: (data) => {
      setAsking(false)
      client.setQueryData([...keys.account, 'overdraft'], data)
      client.invalidateQueries({ queryKey: keys.account })
    },
  })

  if (overdraft.isPending) return <Loading />
  if (overdraft.isError) return <ErrorBox error={overdraft.error} retry={overdraft.refetch} />
  const o = overdraft.data

  return (
    <div className="stack-lg">
      <PageHeader title="Cheque especial" back="/credito" />
      <Card className="stack">
        <div className="grid-2">
          <div>
            <p className="muted small">Seu limite</p>
            <p className="big">{brl(o.limit)}</p>
          </div>
          <div>
            <p className="muted small">Em uso</p>
            <p className={`big ${o.used > 0 ? 'amount-out' : ''}`}>{brl(o.used)}</p>
          </div>
        </div>
        {o.limit > 0 && <progress max={o.limit} value={o.used} aria-label="Uso do limite" />}
        <p className="muted small">
          Juros de {percent(o.monthlyRate)} ao mês, cobrados por dia só sobre o valor usado. Use para emergências: o empréstimo pessoal costuma
          sair mais barato.
        </p>
      </Card>
      <Card>
        {o.maxLimitOffered === 0 ? (
          <p className="muted">No momento não há limite disponível para você. Ele depende do seu score e de não haver parcelas em atraso.</p>
        ) : (
          <form
            className="stack"
            onSubmit={(e) => {
              e.preventDefault()
              save.reset()
              setAsking(true)
            }}
          >
            <h2>{o.limit === 0 ? 'Ativar' : 'Ajustar'} limite</h2>
            <p className="muted small">Disponível para você: até {brl(o.maxLimitOffered)}. Para desativar, informe 0.</p>
            <Field label="Novo limite (R$)" inputMode="decimal" required value={limit} onChange={(e) => setLimit(e.target.value)} />
            <Button disabled={Number.isNaN(parseAmount(limit))}>Salvar</Button>
          </form>
        )}
        <Alert kind="success">{save.isSuccess ? 'Limite atualizado.' : ''}</Alert>
        <PinDialog open={asking} busy={save.isPending} error={save.error?.message} onConfirm={(pin) => save.mutate(pin)} onCancel={() => setAsking(false)} />
      </Card>
    </div>
  )
}
