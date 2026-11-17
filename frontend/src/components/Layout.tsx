import { NavLink, Outlet } from 'react-router'
import { useAuth } from '../lib/useAuth'
import { useUnreadCount } from '../lib/queries'
import { useRealtimeNotifications } from '../lib/realtime'

const links = [
  { to: '/', label: 'Início', icon: '⌂', tab: true },
  { to: '/pix', label: 'PIX', icon: '◈', tab: true },
  { to: '/cartoes', label: 'Cartões', icon: '▭', tab: true },
  { to: '/investimentos', label: 'Investir', icon: '↑', tab: true },
  { to: '/pagamentos', label: 'Pagamentos', icon: '▤' },
  { to: '/extrato', label: 'Extrato', icon: '≡' },
  { to: '/credito', label: 'Crédito', icon: '$', tab: true },
  { to: '/seguranca', label: 'Segurança', icon: '⚿' },
]

export default function Layout() {
  const { logout } = useAuth()
  const unread = useUnreadCount().data?.count ?? 0
  const { toast, dismiss } = useRealtimeNotifications()

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="brand-row">
          <span className="logo" aria-hidden>
            V
          </span>
          <strong>Vela</strong>
        </div>
        <nav aria-label="Principal">
          {links.map((l) => (
            <NavLink key={l.to} to={l.to} end={l.to === '/'} className="nav-link">
              <span aria-hidden>{l.icon}</span> {l.label}
            </NavLink>
          ))}
          <NavLink to="/notificacoes" className="nav-link">
            <span aria-hidden>🔔</span> Notificações {unread > 0 && <span className="badge">{unread}</span>}
          </NavLink>
        </nav>
        <button className="btn btn-ghost logout" onClick={logout}>
          Sair
        </button>
      </aside>

      <main className="content">
        <Outlet />
      </main>

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

      <nav className="tabbar tabbar-5" aria-label="Principal">
        {links.filter((l) => l.tab).map((l) => (
          <NavLink key={l.to} to={l.to} end={l.to === '/'} className="tab">
            <span aria-hidden>{l.icon}</span>
            {l.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
