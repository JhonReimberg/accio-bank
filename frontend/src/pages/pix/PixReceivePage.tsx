import { useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Alert, Button, Card, Empty, Field, Loading, PageHeader, Select } from '../../components/ui'
import { api } from '../../lib/api'
import { brl, keyTypeLabel, parseAmount } from '../../lib/format'
import { keys } from '../../lib/queries'
import type { PixKey, QrCode } from '../../lib/types'

export default function PixReceivePage() {
  const myKeys = useQuery({ queryKey: [...keys.pix, 'keys'], queryFn: () => api<PixKey[]>('/pix/keys') })
  const [keyId, setKeyId] = useState('')
  const [amount, setAmount] = useState('')
  const [message, setMessage] = useState('')
  const [copied, setCopied] = useState(false)
  const generate = useMutation({
    mutationFn: () =>
      api<QrCode>('/pix/qrcodes', {
        body: { keyId: keyId || null, amount: amount ? parseAmount(amount) : null, description: message || null },
      }),
    onSuccess: () => setCopied(false),
  })

  if (myKeys.isPending) return <Loading />
  if (myKeys.data?.length === 0)
    return (
      <div className="stack-lg">
        <PageHeader title="Receber" back="/pix" />
        <Card>
          <Empty>
            Para receber por QR Code você precisa de uma chave PIX. <Link to="/pix/chaves">Cadastrar chave</Link>
          </Empty>
        </Card>
      </div>
    )

  const qr = generate.data
  return (
    <div className="stack-lg">
      <PageHeader title="Receber" back="/pix" />
      <Card>
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault()
            generate.mutate()
          }}
        >
          <Select label="Chave" value={keyId || myKeys.data?.[0]?.id || ''} onChange={(e) => setKeyId(e.target.value)}>
            {myKeys.data?.map((k) => (
              <option key={k.id} value={k.id}>
                {keyTypeLabel[k.type]}: {k.value}
              </option>
            ))}
          </Select>
          <div className="grid-2">
            <Field label="Valor (opcional)" inputMode="decimal" value={amount} onChange={(e) => setAmount(e.target.value)} placeholder="Quem paga escolhe" />
            <Field label="Mensagem (opcional)" maxLength={40} value={message} onChange={(e) => setMessage(e.target.value)} />
          </div>
          <Alert kind="error">{generate.error?.message}</Alert>
          <Button busy={generate.isPending}>Gerar QR Code</Button>
        </form>
      </Card>

      {qr && (
        <Card className="stack center">
          <img className="qr" src={`data:image/png;base64,${qr.qrCodePngBase64}`} alt="QR Code PIX" width={240} height={240} />
          <p>{qr.amount ? brl(qr.amount) : 'Valor livre'}</p>
          <p className="muted small mono break">{qr.payload}</p>
          <Button
            variant="ghost"
            onClick={() => navigator.clipboard?.writeText(qr.payload).then(() => setCopied(true))}
          >
            {copied ? 'Copiado!' : 'Copiar código copia e cola'}
          </Button>
        </Card>
      )}
    </div>
  )
}
