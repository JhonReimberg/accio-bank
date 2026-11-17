import {
  useEffect,
  useId,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type SyntheticEvent,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
} from 'react'
import { Link } from 'react-router'
import { brl } from '../lib/format'

export function Button({
  variant = 'primary',
  busy,
  children,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'ghost' | 'danger' | 'link'; busy?: boolean }) {
  return (
    <button className={`btn btn-${variant}`} disabled={busy || props.disabled} aria-busy={busy} {...props}>
      {busy ? 'Aguarde…' : children}
    </button>
  )
}

export function Field({ label, hint, ...props }: InputHTMLAttributes<HTMLInputElement> & { label: string; hint?: string }) {
  const id = useId()
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input id={id} {...props} />
      {hint && <small className="muted">{hint}</small>}
    </div>
  )
}

export function Select({ label, children, ...props }: SelectHTMLAttributes<HTMLSelectElement> & { label: string }) {
  const id = useId()
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <select id={id} {...props}>
        {children}
      </select>
    </div>
  )
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <section className={`card ${className}`}>{children}</section>
}

export function PageHeader({ title, back, children }: { title: string; back?: string; children?: ReactNode }) {
  return (
    <header className="page-header">
      {back && (
        <Link to={back} className="back" aria-label="Voltar">
          ←
        </Link>
      )}
      <h1>{title}</h1>
      <div className="page-actions">{children}</div>
    </header>
  )
}

export function Loading({ label = 'Carregando…' }: { label?: string }) {
  return (
    <p className="state" role="status">
      <span className="spinner" aria-hidden /> {label}
    </p>
  )
}

export function ErrorBox({ error, retry }: { error: unknown; retry?: () => void }) {
  return (
    <div className="state error-box" role="alert">
      <p>{error instanceof Error ? error.message : 'Não foi possível carregar.'}</p>
      {retry && (
        <Button variant="ghost" onClick={retry}>
          Tentar de novo
        </Button>
      )}
    </div>
  )
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="state muted">{children}</p>
}

export function Alert({ kind, children }: { kind: 'error' | 'success' | 'info'; children: ReactNode }) {
  if (!children) return null
  return (
    <p className={`alert alert-${kind}`} role={kind === 'error' ? 'alert' : 'status'}>
      {children}
    </p>
  )
}

export function Amount({ value, hidden }: { value: number; hidden?: boolean }) {
  return <span className={value < 0 ? 'amount-out' : 'amount-in'}>{hidden ? 'R$ ••••' : `${value > 0 ? '+' : ''}${brl(value)}`}</span>
}

/** Modal asking for the 4-digit transaction PIN before a money operation. */
export function PinDialog({
  open,
  title = 'Confirme com sua senha transacional',
  error,
  busy,
  onConfirm,
  onCancel,
}: {
  open: boolean
  title?: string
  error?: string
  busy?: boolean
  onConfirm: (pin: string) => void
  onCancel: () => void
}) {
  const [pin, setPin] = useState('')
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  function submit(e: SyntheticEvent) {
    e.preventDefault()
    if (pin.length === 4) onConfirm(pin)
  }

  return (
    <dialog ref={ref} className="dialog" onClose={onCancel} onCancel={onCancel}>
      <form onSubmit={submit} className="stack">
        <h2>{title}</h2>
        <Field
          label="Senha de 4 dígitos"
          type="password"
          inputMode="numeric"
          autoComplete="off"
          maxLength={4}
          pattern="[0-9]{4}"
          required
          autoFocus
          value={pin}
          onChange={(e) => setPin(e.target.value.replace(/\D/g, ''))}
        />
        <Alert kind="error">{error}</Alert>
        <div className="row">
          <Button type="button" variant="ghost" onClick={onCancel}>
            Cancelar
          </Button>
          <Button busy={busy} disabled={pin.length !== 4}>
            Confirmar
          </Button>
        </div>
      </form>
    </dialog>
  )
}

export function Pagination({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (page: number) => void }) {
  if (totalPages <= 1) return null
  return (
    <nav className="pagination" aria-label="Paginação">
      <Button variant="ghost" disabled={page <= 1} onClick={() => onChange(page - 1)}>
        Anterior
      </Button>
      <span className="muted">
        Página {page} de {totalPages}
      </span>
      <Button variant="ghost" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
        Próxima
      </Button>
    </nav>
  )
}
