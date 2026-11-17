import {
  useEffect,
  useId,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type PointerEvent,
  type SyntheticEvent,
  type InputHTMLAttributes,
  type ReactNode,
  type SelectHTMLAttributes,
} from 'react'
import { Link } from 'react-router'
import { brl, initials } from '../lib/format'
import { useAvatar } from '../lib/avatar'
import { Mascot } from './Mascot'

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

/** Profile picture if the user set one, initials otherwise. */
export function AvatarFace({ name }: { name?: string }) {
  const photo = useAvatar()
  return photo ? <img className="avatar-img" src={photo} alt="" /> : <>{initials(name)}</>
}

/** Menu-style row: icon, title, optional subtitle, chevron. Each one opens its own screen. */
export function ListRow({ to, icon, title, text, right }: { to: string; icon?: ReactNode; title: string; text?: ReactNode; right?: ReactNode }) {
  return (
    <Link to={to} className="list-row">
      {icon && (
        <span className="list-row-icon" aria-hidden>
          {icon}
        </span>
      )}
      <span className="list-row-body">
        <strong>{title}</strong>
        {text && <span className="muted small">{text}</span>}
      </span>
      {right}
      <span className="chevron" aria-hidden>
        ›
      </span>
    </Link>
  )
}

export function RowGroup({ title, children }: { title?: string; children: ReactNode }) {
  return (
    <section className="row-group">
      {title && <p className="row-group-title">{title}</p>}
      <nav className="row-group-list">{children}</nav>
    </section>
  )
}

/** Horizontal strip of round shortcuts (Área PIX, Pagar, Depositar…). */
export function CircleActions({ items, label }: { items: { to: string; icon: ReactNode; label: string; badge?: string }[]; label: string }) {
  return (
    <nav className="circle-actions" aria-label={label}>
      {items.map((a) => (
        <Link key={a.to + a.label} to={a.to} className="circle-action">
          <span className="circle" aria-hidden>
            {a.icon}
            {a.badge && <em>{a.badge}</em>}
          </span>
          {a.label}
        </Link>
      ))}
    </nav>
  )
}

/** Home section: tappable title with chevron, content below, divider between sections. */
export function Section({ title, to, children }: { title: string; to?: string; children?: ReactNode }) {
  return (
    <section className="home-section">
      {to ? (
        <Link to={to} className="home-section-title">
          <h2>{title}</h2>
          <span className="chevron" aria-hidden>
            ›
          </span>
        </Link>
      ) : (
        <h2 className="home-section-title">{title}</h2>
      )}
      {children}
    </section>
  )
}

/** Scroll-snap carousel with dots, for banners. */
export function Carousel({ children, label }: { children: ReactNode[]; label: string }) {
  const [index, setIndex] = useState(0)
  return (
    <div className="carousel" aria-label={label} role="region">
      <div className="carousel-track" onScroll={(e) => setIndex(Math.round(e.currentTarget.scrollLeft / e.currentTarget.clientWidth))}>
        {children}
      </div>
      {children.length > 1 && (
        <div className="carousel-dots" aria-hidden>
          {children.map((_, i) => (
            <i key={i} className={i === index ? 'on' : ''} />
          ))}
        </div>
      )}
    </div>
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
  return (
    <div className="state muted">
      <Mascot mood="think" size={84} />
      <p>{children}</p>
    </div>
  )
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

/** Card artwork. Material by kind: credit = holographic foil, virtual = glass, debit = black metal. */
export function BankCardVisual({
  credit,
  virtual,
  blocked,
  label,
  number,
  holder,
  expiry,
}: {
  credit?: boolean
  virtual?: boolean
  blocked?: boolean
  label: string
  number: string
  holder: string
  expiry: string
}) {
  const ref = useRef<HTMLDivElement>(null)

  // Pointer moves the light and tilts the card; CSS turns it off for reduced motion.
  function move(e: PointerEvent) {
    const el = ref.current
    if (!el) return
    const r = el.getBoundingClientRect()
    const x = (e.clientX - r.left) / r.width
    const y = (e.clientY - r.top) / r.height
    el.style.setProperty('--mx', `${x * 100}%`)
    el.style.setProperty('--my', `${y * 100}%`)
    el.style.setProperty('--ry', `${(x - 0.5) * 14}deg`)
    el.style.setProperty('--rx', `${(0.5 - y) * 10}deg`)
  }

  function leave() {
    ref.current?.style.removeProperty('--rx')
    ref.current?.style.removeProperty('--ry')
  }

  const kind = [credit && 'bank-card-credit', virtual && !credit && 'bank-card-virtual', blocked && 'bank-card-blocked'].filter(Boolean).join(' ')

  return (
    <div className="bank-card-stage">
      <div ref={ref} className={`bank-card ${kind}`} onPointerMove={move} onPointerLeave={leave} role="img" aria-label={`Cartão ${label} final ${number.slice(-4)}`}>
        <div className="bank-card-top">
          <span className="bank-card-brand">accio</span>
          <span className="bank-card-kind">{label}</span>
        </div>
        <div className="bank-card-mid">
          {!virtual && <span className="bank-card-chip" />}
          <svg className="bank-card-contactless" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            <path d="M8.5 8.5a5 5 0 0 1 0 7M12 6a8.5 8.5 0 0 1 0 12M15.5 3.5a12 12 0 0 1 0 17" />
          </svg>
        </div>
        <p className="bank-card-number">{number}</p>
        <div className="bank-card-foot">
          <span>
            <small>Titular</small>
            {holder}
          </span>
          <span>
            <small>Validade</small>
            {expiry}
          </span>
        </div>
      </div>
    </div>
  )
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
