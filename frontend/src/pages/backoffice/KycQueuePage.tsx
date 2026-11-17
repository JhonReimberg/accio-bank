import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Empty, ErrorBox, Field, Loading, PageHeader } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, dateOnly, dateTime } from '../../lib/format'
import type { KycDetail, KycQueueItem, Paged } from '../../lib/types'

export default function KycQueuePage() {
  const queue = useQuery({ queryKey: ['backoffice', 'kyc'], queryFn: () => api<Paged<KycQueueItem>>('/admin/kyc?pageSize=50') })
  const [selected, setSelected] = useState<string | null>(null)

  return (
    <div className="stack-lg">
      <PageHeader title="Fila de KYC" />
      <Card>
        {queue.isPending ? (
          <Loading />
        ) : queue.isError ? (
          <ErrorBox error={queue.error} retry={queue.refetch} />
        ) : queue.data.items.length === 0 ? (
          <Empty>Nenhum cadastro aguardando análise.</Empty>
        ) : (
          <ul className="list list-clickable">
            {queue.data.items.map((k) => (
              <li key={k.customerId}>
                <button onClick={() => setSelected(k.customerId)}>
                  <div>
                    <p>
                      <strong>{k.fullName}</strong> · {k.email}
                    </p>
                    <p className="muted small">
                      CPF {k.maskedCpf} · renda {brl(k.monthlyIncome ?? 0)} · enviado {k.submittedAt && dateTime(k.submittedAt)}
                    </p>
                  </div>
                  <span aria-hidden>›</span>
                </button>
              </li>
            ))}
          </ul>
        )}
      </Card>
      {selected && <Review customerId={selected} onDone={() => setSelected(null)} />}
    </div>
  )
}

function Review({ customerId, onDone }: { customerId: string; onDone: () => void }) {
  const client = useQueryClient()
  const detail = useQuery({ queryKey: ['backoffice', 'kyc', customerId], queryFn: () => api<KycDetail>(`/admin/kyc/${customerId}`) })
  const [reason, setReason] = useState('')
  const decide = useMutation({
    mutationFn: (approve: boolean) =>
      approve ? api(`/admin/kyc/${customerId}/approve`, { method: 'POST' }) : api(`/admin/kyc/${customerId}/reject`, { body: { reason } }),
    onSuccess: () => {
      client.invalidateQueries({ queryKey: ['backoffice'] })
      onDone()
    },
  })

  if (detail.isPending) return <Loading />
  if (detail.isError) return <ErrorBox error={detail.error} />
  const k = detail.data

  return (
    <Card className="stack detail">
      <div className="row-between">
        <h2>{k.fullName}</h2>
        <Button variant="ghost" onClick={onDone}>
          Fechar
        </Button>
      </div>
      <dl className="summary">
        <dt>CPF</dt>
        <dd>{k.cpf}</dd>
        <dt>Nascimento</dt>
        <dd>{k.birthDate && dateOnly(k.birthDate)}</dd>
        <dt>Celular</dt>
        <dd>{k.phone}</dd>
        <dt>Endereço</dt>
        <dd>{k.address && `${k.address.street}, ${k.address.number} · ${k.address.neighborhood} · ${k.address.city}/${k.address.state}`}</dd>
        <dt>Renda</dt>
        <dd>
          {brl(k.monthlyIncome ?? 0)} · {k.occupation}
        </dd>
      </dl>
      <div className="docs">
        {k.documents.map((d) => (
          <DocumentPreview key={d.id} customerId={customerId} documentId={d.id} label={d.type === 'Selfie' ? 'Selfie' : 'Documento'} contentType={d.contentType} />
        ))}
      </div>
      <p className="muted small">Abrir os dados e os documentos fica registrado no log de auditoria.</p>
      <Field label="Motivo (para reprovar)" maxLength={300} value={reason} onChange={(e) => setReason(e.target.value)} />
      <Alert kind="error">{decide.error?.message}</Alert>
      <div className="row">
        <Button variant="danger" disabled={!reason.trim()} busy={decide.isPending && decide.variables === false} onClick={() => decide.mutate(false)}>
          Reprovar
        </Button>
        <Button busy={decide.isPending && decide.variables === true} onClick={() => decide.mutate(true)}>
          Aprovar
        </Button>
      </div>
    </Card>
  )
}

/** Documents are behind auth, so they are fetched as blobs and shown through object URLs. */
function DocumentPreview({ customerId, documentId, label, contentType }: { customerId: string; documentId: string; label: string; contentType: string }) {
  const blob = useQuery({
    queryKey: ['backoffice', 'kyc-doc', documentId],
    queryFn: () => api<Blob>(`/admin/kyc/${customerId}/documents/${documentId}`, { blob: true }),
    staleTime: Infinity,
  })
  const url = useMemo(() => (blob.data ? URL.createObjectURL(blob.data) : null), [blob.data])
  useEffect(() => () => (url ? URL.revokeObjectURL(url) : undefined), [url])

  return (
    <figure className="doc">
      {url && contentType.startsWith('image/') ? (
        <img src={url} alt={label} />
      ) : url ? (
        <a href={url} target="_blank" rel="noreferrer">
          Abrir PDF
        </a>
      ) : (
        <Loading />
      )}
      <figcaption className="muted small">{label}</figcaption>
    </figure>
  )
}
