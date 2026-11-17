import { useState, type SyntheticEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, ErrorBox, Field, Loading, PageHeader } from '../components/ui'
import { api } from '../lib/api'
import { useAuth } from '../lib/useAuth'
import { dateTime } from '../lib/format'
import { keys } from '../lib/queries'
import type { Device, LoginAttempt, Paged, SecurityOverview, TwoFactorSetup } from '../lib/types'

const failureLabel: Record<string, string> = {
  wrong_password: 'senha errada',
  wrong_2fa_code: 'código 2FA errado',
  locked_out: 'acesso bloqueado',
  unknown_email: 'e-mail desconhecido',
}

export default function SecurityPage() {
  const overview = useQuery({ queryKey: [...keys.security, 'overview'], queryFn: () => api<SecurityOverview>('/security') })
  return (
    <div className="stack-lg">
      <PageHeader title="Segurança" />
      {overview.isPending ? <Loading /> : overview.isError ? <ErrorBox error={overview.error} /> : <TwoFactor enabled={overview.data.twoFactorEnabled} />}
      <ChangePin />
      <Devices />
      <AccessHistory />
    </div>
  )
}

function TwoFactor({ enabled }: { enabled: boolean }) {
  const client = useQueryClient()
  const [setup, setSetup] = useState<TwoFactorSetup | null>(null)
  const [code, setCode] = useState('')
  const refresh = () => client.invalidateQueries({ queryKey: keys.security })
  const begin = useMutation({ mutationFn: () => api<TwoFactorSetup>('/security/2fa/setup', { method: 'POST' }), onSuccess: setSetup })
  const confirm = useMutation({
    mutationFn: () => api(enabled ? '/security/2fa/disable' : '/security/2fa/enable', { body: { code } }),
    onSuccess: () => (setSetup(null), setCode(''), refresh()),
  })

  return (
    <Card className="stack">
      <h2>Verificação em duas etapas</h2>
      <p className="muted">
        {enabled
          ? 'Ativa. Ao entrar em um dispositivo não confiável pedimos o código do aplicativo autenticador.'
          : 'Proteja sua conta com um código do Google Authenticator (ou similar) além da senha.'}
      </p>
      {!enabled && !setup && (
        <Button busy={begin.isPending} onClick={() => begin.mutate()}>
          Ativar
        </Button>
      )}
      {setup && (
        <div className="stack center">
          <p>1. Escaneie o QR Code no aplicativo autenticador:</p>
          <img className="qr" src={`data:image/png;base64,${setup.qrCodePngBase64}`} alt="QR Code do autenticador" width={200} height={200} />
          <p className="muted small">
            Ou digite a chave: <span className="mono">{setup.secret}</span>
          </p>
          <p>2. Digite o código de 6 dígitos que aparece no app:</p>
        </div>
      )}
      {(setup || enabled) && (
        <form
          className="row"
          onSubmit={(e: SyntheticEvent) => {
            e.preventDefault()
            confirm.mutate()
          }}
        >
          <Field label="Código" inputMode="numeric" maxLength={6} pattern="[0-9]{6}" required value={code} onChange={(e) => setCode(e.target.value)} />
          <Button variant={enabled ? 'danger' : 'primary'} busy={confirm.isPending}>
            {enabled ? 'Desativar' : 'Confirmar'}
          </Button>
        </form>
      )}
      <Alert kind="error">{begin.error?.message || confirm.error?.message}</Alert>
    </Card>
  )
}

function ChangePin() {
  const [error, setError] = useState('')
  const change = useMutation({ mutationFn: (body: { password: string; pin: string }) => api('/security/pin', { method: 'PUT', body }) })

  function submit(e: SyntheticEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = e.currentTarget
    const data = new FormData(form)
    if (data.get('pin') !== data.get('confirm')) return setError('As senhas transacionais não conferem.')
    setError('')
    change.mutate({ password: String(data.get('password')), pin: String(data.get('pin')) }, { onSuccess: () => form.reset() })
  }

  return (
    <Card>
      <form className="stack" onSubmit={submit}>
        <h2>Senha transacional</h2>
        <Field label="Senha de acesso" name="password" type="password" autoComplete="current-password" required />
        <div className="grid-2">
          <Field label="Nova senha (4 dígitos)" name="pin" type="password" inputMode="numeric" maxLength={4} pattern="[0-9]{4}" required />
          <Field label="Repita" name="confirm" type="password" inputMode="numeric" maxLength={4} pattern="[0-9]{4}" required />
        </div>
        <Alert kind="error">{error || change.error?.message}</Alert>
        <Alert kind="success">{change.isSuccess ? 'Senha transacional alterada.' : ''}</Alert>
        <Button variant="ghost" busy={change.isPending}>
          Alterar
        </Button>
      </form>
    </Card>
  )
}

function Devices() {
  const client = useQueryClient()
  const { logout } = useAuth()
  const devices = useQuery({ queryKey: [...keys.security, 'devices'], queryFn: () => api<Device[]>('/security/devices') })
  const refresh = () => client.invalidateQueries({ queryKey: [...keys.security, 'devices'] })
  const trust = useMutation({ mutationFn: (id: string) => api(`/security/devices/${id}/trust`, { method: 'POST' }), onSuccess: refresh })
  const remove = useMutation({ mutationFn: (id: string) => api(`/security/devices/${id}`, { method: 'DELETE' }), onSuccess: refresh })
  const revokeAll = useMutation({ mutationFn: () => api('/security/sessions/revoke-all', { method: 'POST' }), onSuccess: logout })

  return (
    <Card className="stack">
      <h2>Dispositivos</h2>
      {devices.isPending ? (
        <Loading />
      ) : devices.isError ? (
        <ErrorBox error={devices.error} retry={devices.refetch} />
      ) : (
        <ul className="list">
          {devices.data.map((d) => (
            <li key={d.id}>
              <div>
                <p>
                  <strong>{d.name}</strong> {d.current && <span className="badge-soft">este dispositivo</span>}{' '}
                  {d.trusted && <span className="badge-soft">confiável</span>}
                </p>
                <p className="muted small">
                  Último acesso {dateTime(d.lastSeenAt)}
                  {d.lastIpAddress && ` · IP ${d.lastIpAddress}`}
                </p>
              </div>
              <div className="row">
                {!d.trusted && (
                  <Button variant="ghost" onClick={() => trust.mutate(d.id)}>
                    Confiar
                  </Button>
                )}
                {!d.current && (
                  <Button variant="danger" onClick={() => remove.mutate(d.id)}>
                    Remover
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
      <Alert kind="error">{trust.error?.message || remove.error?.message}</Alert>
      <Button variant="danger" busy={revokeAll.isPending} onClick={() => revokeAll.mutate()}>
        Sair de todos os dispositivos
      </Button>
    </Card>
  )
}

function AccessHistory() {
  const history = useQuery({
    queryKey: [...keys.security, 'history'],
    queryFn: () => api<Paged<LoginAttempt>>('/security/access-history?pageSize=10'),
  })
  return (
    <Card className="stack">
      <h2>Histórico de acessos</h2>
      {history.isPending ? (
        <Loading />
      ) : history.isError ? (
        <ErrorBox error={history.error} retry={history.refetch} />
      ) : (
        <ul className="list">
          {history.data.items.map((a, i) => (
            <li key={i}>
              <div>
                <p className={a.succeeded ? '' : 'amount-out'}>
                  {a.succeeded ? 'Acesso permitido' : `Tentativa recusada (${failureLabel[a.failureReason ?? ''] ?? a.failureReason})`}
                </p>
                <p className="muted small">
                  {dateTime(a.createdAt)}
                  {a.device && ` · ${a.device}`}
                  {a.ipAddress && ` · IP ${a.ipAddress}`}
                </p>
              </div>
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}
