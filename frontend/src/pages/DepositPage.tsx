import { useState, type SyntheticEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Alert, Button, Card, Field, PageHeader } from '../components/ui'
import { api } from '../lib/api'
import { brl, parseAmount } from '../lib/format'
import { useMoneyMoved } from '../lib/queries'

export default function DepositPage() {
  const moneyMoved = useMoneyMoved()
  const [amount, setAmount] = useState('')
  const [done, setDone] = useState('')
  const deposit = useMutation({
    mutationFn: (value: number) => api('/account/deposits', { body: { amount: value }, idempotent: true }),
    onSuccess: (_, value) => {
      moneyMoved()
      setDone(`Depósito de ${brl(value)} feito.`)
      setAmount('')
    },
  })
  const value = parseAmount(amount)

  function submit(e: SyntheticEvent) {
    e.preventDefault()
    setDone('')
    if (!Number.isNaN(value)) deposit.mutate(value)
  }

  return (
    <div className="stack-lg">
      <PageHeader title="Depositar" back="/" />
      <Card>
        <form className="stack" onSubmit={submit}>
          <p className="muted">Depósito simulado para testar o app: o dinheiro "entra" na conta a partir do caixa do banco (até R$ 10.000 por vez).</p>
          <Field label="Valor (R$)" inputMode="decimal" required value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="100,00" />
          <Alert kind="error">{deposit.error?.message}</Alert>
          <Alert kind="success">{done}</Alert>
          <Button busy={deposit.isPending} disabled={Number.isNaN(value)}>
            Depositar
          </Button>
        </form>
      </Card>
    </div>
  )
}
