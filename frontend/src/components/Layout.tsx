import { Link, NavLink, Outlet, useLocation } from 'react-router'
import { useAuth } from '../lib/useAuth'
import { useAccount, useUnreadCount } from '../lib/queries'
import { useRealtimeNotifications } from '../lib/realtime'
import { toggleHidden, useHidden } from '../lib/hideBalance'
import { AvatarFace } from './ui'
import { useEffect, useState } from 'react'
import { Logo } from './Mascot'

// 24px line icons (stroke), so the top bar matches the thin icons of the reference app.
const EYE = 'M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6Z'
const EYE_OFF = 'M3 3l18 18 M10.6 5.1A10 10 0 0 1 12 5c6.4 0 10 7 10 7a17 17 0 0 1-3.2 4.1 M6.6 6.6A17 17 0 0 0 2 12s3.6 7 10 7a9.7 9.7 0 0 0 5.4-1.6 M9.9 9.9a3 3 0 0 0 4.2 4.2'
const HELP = 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20Z M9.1 9a3 3 0 0 1 5.8 1c0 2-3 3-3 3 M12 17h.01'
const MENU = 'M4 6h16 M4 12h16 M4 18h16'

function Icon({ d }: { d: string }) {
  return (
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden>
      <path d={d} />
    </svg>
  )
}

function greeting() {
  const h = Number(new Intl.DateTimeFormat('pt-BR', { hour: 'numeric', hour12: false, timeZone: 'America/Sao_Paulo' }).format(new Date()))
  return h < 12 ? 'Bom dia' : h < 18 ? 'Boa tarde' : 'Boa noite'
}

type NavItem = { to: string; label: string; icon: string }

const sections: { title: string; links: NavItem[] }[] = [
  {
    title: 'Conta',
    links: [
      { to: '/', label: 'Início', icon: '⌂' },
      { to: '/conta', label: 'Saldo e extrato', icon: '⇅' },
      { to: '/pix', label: 'Área PIX', icon: '◈' },
      { to: '/pagamentos', label: 'Pagamentos', icon: '▤' },
    ],
  },
  {
    title: 'Produtos',
    links: [
      { to: '/cartoes', label: 'Cartões', icon: '▭' },
      { to: '/investimentos', label: 'Investimentos', icon: '$' },
      { to: '/credito', label: 'Empréstimos', icon: '◔' },
      { to: '/beneficios', label: 'Benefícios', icon: '✦' },
      { to: '/recarga', label: 'Recarga', icon: '▯' },
    ],
  },
]

// Bottom bar on phones: icon-only floating pill, like the reference app.
const tabs: NavItem[] = [
  { to: '/', label: 'Início', icon: '⇅' },
  { to: '/investimentos', label: 'Investimentos', icon: '$' },
  { to: '/beneficios', label: 'Benefícios', icon: '✦' },
  { to: '/recarga', label: 'Recarga de celular', icon: '▯' },
]

export default function Layout() {
  const { logout } = useAuth()
  const account = useAccount().data
  const unread = useUnreadCount().data?.count ?? 0
  const hidden = useHidden()
  const { pathname } = useLocation()
  const { toast, dismiss } = useRealtimeNotifications()
  const home = pathname === '/'
  const [scrolled, setScrolled] = useState(false)
  const compact = home && scrolled

  // Home header shrinks once the page scrolls (greeting out, brand in).
  useEffect(() => {
    if (!home) return
    const onScroll = () => setScrolled(window.scrollY > 24)
    onScroll()
    window.addEventListener('scroll', onScroll, { passive: true })
    return () => window.removeEventListener('scroll', onScroll)
  }, [home])

  return (
    <div className="app">
      <aside className="sidebar">
        <Link to="/" className="brand-link" aria-label="AccioBank, início">
          <Logo />
        </Link>
        <nav aria-label="Principal">
          {sections.map((s) => (
            <div key={s.title}>
              <p className="nav-section">{s.title}</p>
              {s.links.map((l) => (
                <NavLink key={l.to} to={l.to} end={l.to === '/'} className="nav-link">
                  <span aria-hidden>{l.icon}</span> {l.label}
                </NavLink>
              ))}
            </div>
          ))}
          <div>
            <p className="nav-section">Ajustes</p>
            <NavLink to="/notificacoes" className="nav-link">
              <span aria-hidden>🔔</span> Notificações {unread > 0 && <span className="badge">{unread}</span>}
            </NavLink>
            <NavLink to="/menu" className="nav-link">
              <span aria-hidden>☰</span> Menu completo
            </NavLink>
          </div>
        </nav>
        <Link to="/perfil" className="sidebar-user">
          <span className="avatar">
            <AvatarFace name={account?.name} />
          </span>
          <span>
            <strong>{account?.name.split(' ')[0] ?? 'Perfil'}</strong>
            <small className="muted">Conta {account?.number}</small>
          </span>
        </Link>
        <button className="btn btn-ghost logout" onClick={logout}>
          Sair
        </button>
      </aside>

      <div className="main-col">
        <header className={`topbar ${home ? 'topbar-home' : ''} ${compact ? 'is-compact' : ''}`}>
          <div className="topbar-row">
            <Link to="/perfil" className="avatar" aria-label={`Perfil e conta${unread > 0 ? `, ${unread} notificações não lidas` : ''}`}>
              <AvatarFace name={account?.name} />
              {unread > 0 && <span className="dot" />}
            </Link>
            {/* brand fades in only once the greeting scrolls away */}
            <Link to="/" className="brand-link topbar-brand" aria-label="AccioBank, início" tabIndex={compact ? 0 : -1}>
              <span className="logo-word">
                Accio<b>Bank</b>
              </span>
            </Link>
            <div className="topbar-actions">
              <button className="icon-btn" onClick={toggleHidden} aria-pressed={hidden} aria-label={hidden ? 'Mostrar valores' : 'Esconder valores'}>
                <Icon d={hidden ? EYE_OFF : EYE} />
              </button>
              <Link to="/ajuda" className="icon-btn" aria-label="Ajuda">
                <Icon d={HELP} />
              </Link>
              <Link to="/menu" className="icon-btn" aria-label="Menu">
                <Icon d={MENU} />
              </Link>
            </div>
          </div>
          {home && account && (
            <p className="topbar-hello">
              <span className="muted">{greeting()},</span> {account.name.split(' ')[0]}
            </p>
          )}
        </header>

        <main className="content">
          <Outlet />
        </main>
      </div>

      {toast && (
        <div className="toast" role="status" aria-live="polite">
          <div>
            <strong>{toast.title}</strong>
            <p>{toast.body}</p>
          </div>
          <button className="btn btn-ghost" onClick={dismiss} aria-label="Fechar aviso">
            ×
          </button>
        </div>
      )}

      <nav className="tabbar" aria-label="Principal">
        {tabs.map((l) => (
          <NavLink key={l.to} to={l.to} end={l.to === '/'} className="tab" aria-label={l.label} title={l.label}>
            <span aria-hidden>{l.icon}</span>
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
