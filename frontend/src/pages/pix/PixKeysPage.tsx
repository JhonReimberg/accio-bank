import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Empty, ErrorBox, Field, Loading, PageHeader, Select } from '../../components/ui'
import { api } from '../../lib/api'
import { dateTime, keyTypeLabel } from '../../lib/format'
import { keys } from '../../lib/queries'
import type { PixKey, PixKeyType } from '../../lib/types'

const placeholders: Record<PixKeyType, string> = {
  Cpf: 'Seu CPF',
  Email: 'voce@exemplo.com',
  Phone: '(11) 91234-5678',
  Random: '',
}

export default function PixKeysPage() {
  const client = useQueryClient()
  const list = useQuery({ queryKey: [...keys.pix, 'keys'], queryFn: () => api<PixKey[]>('/pix/keys') })
  const [type, setType] = useState<PixKeyType>('Email')
  const [value, setValue] = useState('')
  const refresh = () => client.invalidateQueries({ queryKey: [...keys.pix, 'keys'] })
  const add = useMutation({
    mutationFn: () => api('/pix/keys', { body: { type, value: type === 'Random' ? null : value } }),
    onSuccess: () => (setValue(''), refresh()),
  })
  const remove = useMutation({ mutationFn: (id: string) => api(`/pix/keys/${id}`, { method: 'DELETE' }), onSuccess: refresh })

  return (
    <div className="stack-lg">
      <PageHeader title="Minhas chaves" back="/pix" />
      <Card>
        {list.isPending ? (
          <Loading />
        ) : list.isError ? (
          <ErrorBox error={list.error} retry={list.refetch} />
        ) : list.data.length === 0 ? (
          <Empty>Você ainda não tem chaves. Cadastre uma para receber PIX.</Empty>
        ) : (
          <ul className="list">
            {list.data.map((k) => (
              <li key={k.id}>
                <div>
                  <p>
                    <strong>{keyTypeLabel[k.type]}</strong> <span className="mono">{k.value}</span>
                  </p>
                  <p className="muted small">Desde {dateTime(k.createdAt)}</p>
                </div>
                <Button variant="danger" busy={remove.isPending && remove.variables === k.id} onClick={() => remove.mutate(k.id)}>
                  Excluir
                </Button>
              </li>
            ))}
          </ul>
        )}
        <Alert kind="error">{remove.error?.message}</Alert>
      </Card>

      <Card>
        <form
          className="stack"
          onSubmit={(e) => {
            e.preventDefault()
            add.mutate()
          }}
        >
          <h2>Nova chave</h2>
          <p className="muted small">Até 5 chaves por conta. A chave CPF precisa ser o seu CPF.</p>
          <Select label="Tipo" value={type} onChange={(e) => setType(e.target.value as PixKeyType)}>
            {Object.entries(keyTypeLabel).map(([v, label]) => (
              <option key={v} value={v}>
                {label}
              </option>
            ))}
          </Select>
          {type !== 'Random' && (
            <Field label="Chave" required maxLength={100} placeholder={placeholders[type]} value={value} onChange={(e) => setValue(e.target.value)} />
          )}
          <Alert kind="error">{add.error?.message}</Alert>
          <Button busy={add.isPending}>{type === 'Random' ? 'Gerar chave aleatória' : 'Cadastrar'}</Button>
        </form>
      </Card>
    </div>
  )
}
