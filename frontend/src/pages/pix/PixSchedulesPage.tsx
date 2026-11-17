import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Alert, Button, Card, Empty, ErrorBox, Loading, PageHeader } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, dateOnly } from '../../lib/format'
import { keys } from '../../lib/queries'
import type { PixSchedule } from '../../lib/types'

const frequencyLabel = { Once: 'Uma vez', Weekly: 'Semanal', Monthly: 'Mensal' }
const statusLabel = { Active: 'Ativo', Completed: 'Concluído', Cancelled: 'Cancelado' }

export default function PixSchedulesPage() {
  const client = useQueryClient()
  const list = useQuery({ queryKey: [...keys.pix, 'schedules'], queryFn: () => api<PixSchedule[]>('/pix/schedules') })
  const cancel = useMutation({
    mutationFn: (id: string) => api(`/pix/schedules/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries({ queryKey: [...keys.pix, 'schedules'] }),
  })

  return (
    <div className="stack-lg">
      <PageHeader title="PIX agendados" back="/pix">
        <Link to="/pix/pagar" className="btn btn-primary">
          Novo
        </Link>
      </PageHeader>
      <Card>
        {list.isPending ? (
          <Loading />
        ) : list.isError ? (
          <ErrorBox error={list.error} retry={list.refetch} />
        ) : list.data.length === 0 ? (
          <Empty>Nenhum agendamento. Para criar, faça um PIX e marque "Agendar ou repetir".</Empty>
        ) : (
          <ul className="list">
            {list.data.map((s) => (
              <li key={s.id}>
                <div>
                  <p>
                    <strong>{brl(s.amount)}</strong> para {s.recipientName}
                  </p>
                  <p className="muted small">
                    {frequencyLabel[s.frequency]} · {statusLabel[s.status]}
                    {s.status === 'Active' && ` · próximo em ${dateOnly(s.nextRunDate)}`} · {s.executions} execução(ões)
                  </p>
                  {s.lastError && <p className="amount-out small">Última tentativa falhou: {s.lastError}</p>}
                </div>
                {s.status === 'Active' && (
                  <Button variant="danger" busy={cancel.isPending && cancel.variables === s.id} onClick={() => cancel.mutate(s.id)}>
                    Cancelar
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
        <Alert kind="error">{cancel.error?.message}</Alert>
      </Card>
    </div>
  )
}
