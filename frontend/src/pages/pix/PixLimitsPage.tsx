import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, ErrorBox, Field, Loading, PageHeader, PinDialog } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, dateTime, parseAmount } from '../../lib/format'
import { keys } from '../../lib/queries'
import type { PixLimits } from '../../lib/types'

export default function PixLimitsPage() {
  const client = useQueryClient()
  const limits = useQuery({ queryKey: [...keys.pix, 'limits'], queryFn: () => api<PixLimits>('/pix/limits') })
  const [day, setDay] = useState('')
  const [night, setNight] = useState('')
  const [asking, setAsking] = useState(false)
  const change = useMutation({
    mutationFn: (pin: string) =>
      api<PixLimits>('/pix/limits', { method: 'PUT', body: { daytimeLimit: parseAmount(day), nighttimeLimit: parseAmount(night), pin } }),
    onSuccess: (data) => {
      setAsking(false)
      client.setQueryData([...keys.pix, 'limits'], data)
    },
  })

  if (limits.isPending) return <Loading />
  if (limits.isError) return <ErrorBox error={limits.error} retry={limits.refetch} />
  const l = limits.data

  return (
    <div className="stack-lg">
      <PageHeader title="Limites PIX" back="/pix" />
      <Card className="stack">
        <div className="grid-2">
          <div>
            <p className="muted small">Diurno (06h–20h)</p>
            <p className="big">{brl(l.daytimeLimit)}</p>
          </div>
          <div>
            <p className="muted small">Noturno (20h–06h)</p>
            <p className="big">{brl(l.nighttimeLimit)}</p>
          </div>
        </div>
        <p className="muted small">
          Agora ({l.isNightNow ? 'noturno' : 'diurno'}): usado {brl(l.usedInCurrentPeriod)}, disponível {brl(l.availableInCurrentPeriod)}.
        </p>
        {l.pendingEffectiveAt && (
          <Alert kind="info">
            Aumento solicitado
            {l.pendingDaytimeLimit !== null && ` (diurno ${brl(l.pendingDaytimeLimit)})`}
            {l.pendingNighttimeLimit !== null && ` (noturno ${brl(l.pendingNighttimeLimit)})`} vale a partir de {dateTime(l.pendingEffectiveAt)}.
          </Alert>
        )}
      </Card>
      <Card>
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault()
            change.reset()
            setAsking(true)
          }}
        >
          <h2>Ajustar limites</h2>
          <p className="muted small">
            Reduções valem na hora. Aumentos valem só depois de 24 horas, para proteger você caso alguém tenha acesso ao seu celular. Máximo da
            sua conta: {brl(l.maxDaytimeLimit)} de dia e {brl(l.maxNighttimeLimit)} à noite.
          </p>
          <div className="grid-2">
            <Field label="Diurno (R$)" inputMode="decimal" required value={day} onChange={(e) => setDay(e.target.value)} placeholder={String(l.daytimeLimit)} />
            <Field label="Noturno (R$)" inputMode="decimal" required value={night} onChange={(e) => setNight(e.target.value)} placeholder={String(l.nighttimeLimit)} />
          </div>
          <Button disabled={Number.isNaN(parseAmount(day)) || Number.isNaN(parseAmount(night))}>Salvar</Button>
        </form>
        <PinDialog open={asking} busy={change.isPending} error={change.error?.message} onConfirm={(pin) => change.mutate(pin)} onCancel={() => setAsking(false)} />
      </Card>
    </div>
  )
}
