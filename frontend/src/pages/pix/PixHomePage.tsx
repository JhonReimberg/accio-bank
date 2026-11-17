import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Card, PageHeader } from '../../components/ui'
import { api } from '../../lib/api'
import { brl } from '../../lib/format'
import { keys } from '../../lib/queries'
import type { PixLimits } from '../../lib/types'

const actions = [
  { to: '/pix/pagar', title: 'Pagar', text: 'Por chave ou copia e cola', icon: '↗' },
  { to: '/pix/receber', title: 'Receber', text: 'Gerar QR Code', icon: '↙' },
  { to: '/pix/chaves', title: 'Minhas chaves', text: 'CPF, e-mail, celular, aleatória', icon: '⚷' },
  { to: '/pix/agendados', title: 'Agendados', text: 'Únicos e recorrentes', icon: '◷' },
  { to: '/pix/limites', title: 'Limites', text: 'Diurno e noturno', icon: '◐' },
]

export default function PixHomePage() {
  const limits = useQuery({ queryKey: [...keys.pix, 'limits'], queryFn: () => api<PixLimits>('/pix/limits') })

  return (
    <div className="stack-lg">
      <PageHeader title="PIX" />
      <div className="action-grid">
        {actions.map((a) => (
          <Link key={a.to} to={a.to} className="action-card">
            <span className="action-icon" aria-hidden>
              {a.icon}
            </span>
            <strong>{a.title}</strong>
            <span className="muted small">{a.text}</span>
          </Link>
        ))}
      </div>
      {limits.data && (
        <Card>
          <p className="muted small">Disponível agora ({limits.data.isNightNow ? 'período noturno' : 'período diurno'})</p>
          <p className="big">{brl(limits.data.availableInCurrentPeriod)}</p>
        </Card>
      )}
    </div>
  )
}
