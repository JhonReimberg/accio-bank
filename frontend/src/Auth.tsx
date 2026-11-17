import { useState, type FormEvent } from 'react'
import { api } from './api'

type Props = { onLogin: (token: string) => void }

export default function Auth({ onLogin }: Props) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const isRegister = mode === 'register'

  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    const data = Object.fromEntries(new FormData(e.currentTarget))
    setBusy(true)
    setError('')
    try {
      const res = await api<{ session: { accessToken: string } | null }>(`/auth/${mode}`, data)
      if (!res.session) throw new Error('Esta conta usa verificação em duas etapas.')
      onLogin(res.session.accessToken)
    } catch (err) {
      setError((err as Error).message)
      setBusy(false)
    }
  }

  return (
    <main className="shell auth">
      <div className="brand">
        <span className="logo" aria-hidden>
          V
        </span>
        <h1>Vela</h1>
        <p className="muted">Seu banco, sem fila e sem tarifa.</p>
      </div>

      <form className="card form" onSubmit={submit}>
        <h2>{isRegister ? 'Abrir minha conta' : 'Entrar'}</h2>
        {isRegister && (
          <label>
            Nome
            <input name="name" autoComplete="name" required minLength={2} maxLength={100} />
          </label>
        )}
        <label>
          E-mail
          <input name="email" type="email" autoComplete="email" required maxLength={200} />
        </label>
        <label>
          Senha
          <input
            name="password"
            type="password"
            autoComplete={isRegister ? 'new-password' : 'current-password'}
            required
            minLength={isRegister ? 8 : undefined}
          />
        </label>
        <p className="error" role="alert">
          {error}
        </p>
        <button className="primary" disabled={busy}>
          {busy ? 'Aguarde…' : isRegister ? 'Criar conta' : 'Entrar'}
        </button>
        <button
          type="button"
          className="link"
          onClick={() => {
            setMode(isRegister ? 'login' : 'register')
            setError('')
          }}
        >
          {isRegister ? 'Já tenho conta' : 'Ainda não tenho conta'}
        </button>
      </form>
    </main>
  )
}
