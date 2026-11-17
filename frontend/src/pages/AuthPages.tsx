import { useState, type SyntheticEvent, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router'
import { Alert, Button, Field } from '../components/ui'
import { Logo, Mascot } from '../components/Mascot'
import { useAuth } from '../lib/useAuth'

function AuthShell({ children }: { children: ReactNode }) {
  return (
    <div className="auth-page">
      <aside className="auth-side">
        <Mascot mood="hello" size={260} />
        <h2>Accio, dinheiro!</h2>
        <p className="muted">O feitiço que chama seu dinheiro: PIX, cartões, caixinhas e crédito em um só lugar.</p>
      </aside>
      <main className="auth-shell">
        <div className="auth-hero">
          <Mascot mood="hello" size={150} />
          <Logo size={30} />
          <p className="muted">Banco digital fictício · projeto de portfólio</p>
        </div>
        <div className="card stack">{children}</div>
      </main>
    </div>
  )
}

export function LoginPage() {
  const { login, completeTwoFactor } = useAuth()
  const navigate = useNavigate()
  const [challenge, setChallenge] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function run(action: () => Promise<void>) {
    setBusy(true)
    setError('')
    try {
      await action()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  function submitPassword(e: SyntheticEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    run(async () => {
      const token = await login(String(form.get('email')), String(form.get('password')))
      if (token) setChallenge(token)
      else navigate('/')
    })
  }

  function submitCode(e: SyntheticEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    run(async () => {
      await completeTwoFactor(challenge!, String(form.get('code')), form.get('trust') === 'on')
      navigate('/')
    })
  }

  return (
    <AuthShell>
      {challenge ? (
        <form className="stack" onSubmit={submitCode}>
          <h2>Verificação em duas etapas</h2>
          <p className="muted">Digite o código de 6 dígitos do seu aplicativo autenticador.</p>
          <Field label="Código" name="code" inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} required autoFocus />
          <label className="check">
            <input type="checkbox" name="trust" /> Confiar neste dispositivo (não pedir o código de novo)
          </label>
          <Alert kind="error">{error}</Alert>
          <Button busy={busy}>Verificar</Button>
          <Button type="button" variant="link" onClick={() => setChallenge(null)}>
            Voltar
          </Button>
        </form>
      ) : (
        <form className="stack" onSubmit={submitPassword}>
          <h2>Entrar</h2>
          <Field label="E-mail" name="email" type="email" autoComplete="email" required />
          <Field label="Senha" name="password" type="password" autoComplete="current-password" required />
          <Alert kind="error">{error}</Alert>
          <Button busy={busy}>Entrar</Button>
          <p className="muted center">
            Ainda não tem conta? <Link to="/abrir-conta">Abra a sua</Link>
          </p>
        </form>
      )}
    </AuthShell>
  )
}

export function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function submit(e: SyntheticEvent<HTMLFormElement>) {
    e.preventDefault()
    const form = new FormData(e.currentTarget)
    if (form.get('password') !== form.get('confirm')) {
      setError('As senhas não conferem.')
      return
    }
    setBusy(true)
    setError('')
    try {
      await register(String(form.get('name')), String(form.get('email')), String(form.get('password')))
      navigate('/cadastro')
    } catch (err) {
      setError((err as Error).message)
      setBusy(false)
    }
  }

  return (
    <AuthShell>
      <form className="stack" onSubmit={submit}>
        <h2>Abrir conta</h2>
        <p className="muted">Primeiro crie seu acesso. Em seguida pedimos seus dados e documentos.</p>
        <Field label="Como quer ser chamado(a)?" name="name" autoComplete="given-name" required minLength={2} maxLength={100} />
        <Field label="E-mail" name="email" type="email" autoComplete="email" required maxLength={200} />
        <Field label="Senha" name="password" type="password" autoComplete="new-password" required minLength={8} hint="Mínimo de 8 caracteres." />
        <Field label="Confirme a senha" name="confirm" type="password" autoComplete="new-password" required minLength={8} />
        <Alert kind="error">{error}</Alert>
        <Button busy={busy}>Continuar</Button>
        <p className="muted center">
          Já tem conta? <Link to="/entrar">Entrar</Link>
        </p>
      </form>
    </AuthShell>
  )
}
