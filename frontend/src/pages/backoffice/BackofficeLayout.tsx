import { NavLink, Outlet } from 'react-router'
import { useAuth } from '../../lib/useAuth'

const links = [
  { to: '/backoffice', label: 'Painel', icon: '▦', end: true },
  { to: '/backoffice/kyc', label: 'Fila de KYC', icon: '☑' },
  { to: '/backoffice/antifraude', label: 'Antifraude', icon: '⚠' },
  { to: '/backoffice/clientes', label: 'Clientes', icon: '☺' },
  { to: '/backoffice/auditoria', label: 'Auditoria', icon: '⌕' },
  { to: '/backoffice/politica', label: 'Política de crédito', icon: '%' },
]

export default function BackofficeLayout() {
  const { state, logout } = useAuth()
  const session = state.status === 'authenticated' ? state.session : null

  return (
    <div className="app backoffice">
      <aside className="sidebar">
        <div className="brand-row">
          <span className="logo" aria-hidden>
            V
          </span>
          <div>
            <strong>Vela</strong>
            <p className="muted small">Back-office · {session?.role === 'Admin' ? 'Admin' : 'Analista'}</p>
          </div>
        </div>
        <nav aria-label="Back-office">
          {links.map((l) => (
            <NavLink key={l.to} to={l.to} end={l.end} className="nav-link">
              <span aria-hidden>{l.icon}</span> {l.label}
            </NavLink>
          ))}
        </nav>
        <button className="btn btn-ghost logout" onClick={logout}>
          Sair
        </button>
      </aside>
      <main className="content content-wide">
        <Outlet />
      </main>
      <nav className="tabbar tabbar-5" aria-label="Back-office">
        {links.map((l) => (
          <NavLink key={l.to} to={l.to} end={l.end} className="tab">
            <span aria-hidden>{l.icon}</span>
            {l.label.split(' ')[0]}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
