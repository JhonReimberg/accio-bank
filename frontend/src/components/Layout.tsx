import { NavLink, Outlet } from 'react-router'
import { useAuth } from '../lib/useAuth'
import { useUnreadCount } from '../lib/queries'

const links = [
  { to: '/', label: 'Início', icon: '⌂' },
  { to: '/pix', label: 'PIX', icon: '◈' },
  { to: '/extrato', label: 'Extrato', icon: '≡' },
  { to: '/seguranca', label: 'Segurança', icon: '⚿' },
]

export default function Layout() {
  const { logout } = useAuth()
  const unread = useUnreadCount().data?.count ?? 0

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

      <nav className="tabbar" aria-label="Principal">
        {links.map((l) => (
          <NavLink key={l.to} to={l.to} end={l.to === '/'} className="tab">
            <span aria-hidden>{l.icon}</span>
            {l.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
