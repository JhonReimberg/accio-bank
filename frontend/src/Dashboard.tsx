import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { api, ApiError, brl, dateTime, type Account, type Tx } from './api'

type Props = { onLogout: () => void }
type Panel = 'deposit' | 'transfer' | null

export default function Dashboard({ onLogout }: Props) {
  const [account, setAccount] = useState<Account | null>(null)
  const [txs, setTxs] = useState<Tx[]>([])
  const [hidden, setHidden] = useState(false)
  const [panel, setPanel] = useState<Panel>(null)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  const fail = useCallback(
    (err: unknown) => {
      if (err instanceof ApiError && err.status === 401) onLogout()
      else setError((err as Error).message)
    },
    [onLogout],
  )

  const load = useCallback(
    () =>
      Promise.all([api<Account>('/account'), api<Tx[]>('/account/transactions')]).then(([acc, list]) => {
        setAccount(acc)
        setTxs(list)
      }, fail),
    [fail],
  )

  useEffect(() => {
    load()
  }, [load])

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    const amount = Number(form.get('amount'))
    setBusy(true)
    setError('')
    try {
      if (panel === 'deposit') {
        await api('/account/deposits', { amount })
        setNotice(`Depósito de ${brl.format(amount)} feito.`)
      } else {
        await api('/account/transfers', { to: form.get('to'), amount })
        setNotice(`Transferência de ${brl.format(amount)} enviada.`)
      }
      setPanel(null)
      await load()
    } catch (err) {
      fail(err)
    } finally {
      setBusy(false)
    }
  }

  function open(next: Panel) {
    setPanel(panel === next ? null : next)
    setError('')
    setNotice('')
  }

  if (!account)
    return (
      <main className="shell">
        <p className="muted" role="status">
          {error || 'Carregando…'}
        </p>
      </main>
    )

  return (
    <main className="shell">
      <header className="topbar">
        <div>
          <p className="muted">Olá,</p>
          <h1>{account.name.split(' ')[0]}</h1>
        </div>
        <button className="ghost" onClick={onLogout}>
          Sair
        </button>
      </header>

      <section className="card balance" aria-label="Saldo">
        <div className="balance-row">
          <span className="muted">Saldo disponível</span>
          <button
            className="ghost small"
            onClick={() => setHidden(!hidden)}
            aria-pressed={hidden}
            aria-label={hidden ? 'Mostrar saldo' : 'Esconder saldo'}
          >
            {hidden ? 'Mostrar' : 'Esconder'}
          </button>
        </div>
        <p className="amount">{hidden ? 'R$ ••••' : brl.format(account.balance)}</p>
        <p className="muted small-text">
          Conta {account.number}{' '}
          <button className="link inline" onClick={() => navigator.clipboard?.writeText(account.number)}>
            copiar
          </button>
        </p>
      </section>

      <nav className="actions">
        <button className={panel === 'deposit' ? 'action active' : 'action'} onClick={() => open('deposit')}>
          <span aria-hidden>＋</span>Depositar
        </button>
        <button className={panel === 'transfer' ? 'action active' : 'action'} onClick={() => open('transfer')}>
          <span aria-hidden>↗</span>Transferir
        </button>
      </nav>

      {panel && (
        <form className="card form" onSubmit={submit} key={panel}>
          <h2>{panel === 'deposit' ? 'Depositar (simulado)' : 'Transferir'}</h2>
          {panel === 'transfer' && (
            <label>
              Para (e-mail ou número da conta)
              <input name="to" required maxLength={200} autoComplete="off" />
            </label>
          )}
          <label>
            Valor (R$)
            <input
              name="amount"
              type="number"
              inputMode="decimal"
              min="0.01"
              max={panel === 'deposit' ? 10000 : undefined}
              step="0.01"
              required
            />
          </label>
          <button className="primary" disabled={busy}>
            {busy ? 'Enviando…' : 'Confirmar'}
          </button>
        </form>
      )}

      <p className="error" role="alert">
        {error}
      </p>
      <p className="notice" role="status">
        {notice}
      </p>

      <section className="card">
        <h2>Extrato</h2>
        {txs.length === 0 ? (
          <p className="muted">Nenhuma movimentação ainda. Que tal fazer um depósito?</p>
        ) : (
          <ul className="statement">
            {txs.map((t) => (
              <li key={t.id}>
                <div>
                  <p>{t.description}</p>
                  <p className="muted small-text">{dateTime.format(new Date(t.createdAt))}</p>
                </div>
                <span className={t.amount < 0 ? 'out' : 'in'}>
                  {t.amount > 0 && '+'}
                  {brl.format(t.amount)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </main>
  )
}
