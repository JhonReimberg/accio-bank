import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router'
import Layout from './components/Layout'
import { Button, Card, ErrorBox, Loading } from './components/ui'
import { useAuth } from './lib/useAuth'
import { useOnboarding } from './lib/queries'
import { LoginPage, RegisterPage } from './pages/AuthPages'
import DepositPage from './pages/DepositPage'
import HomePage from './pages/HomePage'
import NotificationsPage from './pages/NotificationsPage'
import OnboardingPage from './pages/OnboardingPage'
import PixHomePage from './pages/pix/PixHomePage'
import PixKeysPage from './pages/pix/PixKeysPage'
import PixLimitsPage from './pages/pix/PixLimitsPage'
import PixReceivePage from './pages/pix/PixReceivePage'
import PixSchedulesPage from './pages/pix/PixSchedulesPage'
import PixSendPage from './pages/pix/PixSendPage'
import SecurityPage from './pages/SecurityPage'
import StatementPage from './pages/StatementPage'

function PublicOnly() {
  const { state } = useAuth()
  if (state.status === 'loading') return <Loading />
  return state.status === 'authenticated' ? <Navigate to="/" replace /> : <Outlet />
}

function SignedIn() {
  const { state, logout } = useAuth()
  if (state.status === 'loading') return <Loading />
  if (state.status === 'anonymous') return <Navigate to="/entrar" replace />
  if (state.session.role !== 'Customer')
    return (
      <main className="auth-shell">
        <Card className="stack center">
          <h1>Olá, {state.session.name}</h1>
          <p className="muted">
            Você entrou com um perfil de back-office ({state.session.role}). As telas de back-office chegam na Fase 2; por enquanto use a
            API documentada em /scalar/v1.
          </p>
          <Button variant="ghost" onClick={logout}>
            Sair
          </Button>
        </Card>
      </main>
    )
  return <Outlet />
}

/** Banking screens need an approved account; anything else goes to the onboarding flow. */
function ActiveAccount() {
  const onboarding = useOnboarding()
  if (onboarding.isPending) return <Loading />
  if (onboarding.isError) return <ErrorBox error={onboarding.error} retry={onboarding.refetch} />
  const { nextStep, accountStatus } = onboarding.data
  if (nextStep !== 'Done' || accountStatus === 'PendingReview') return <Navigate to="/cadastro" replace />
  return <Outlet />
}

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<PublicOnly />}>
          <Route path="/entrar" element={<LoginPage />} />
          <Route path="/abrir-conta" element={<RegisterPage />} />
        </Route>
        <Route element={<SignedIn />}>
          <Route path="/cadastro" element={<OnboardingPage />} />
          <Route element={<ActiveAccount />}>
            <Route element={<Layout />}>
              <Route index element={<HomePage />} />
              <Route path="depositar" element={<DepositPage />} />
              <Route path="extrato" element={<StatementPage />} />
              <Route path="pix" element={<PixHomePage />} />
              <Route path="pix/pagar" element={<PixSendPage />} />
              <Route path="pix/receber" element={<PixReceivePage />} />
              <Route path="pix/chaves" element={<PixKeysPage />} />
              <Route path="pix/agendados" element={<PixSchedulesPage />} />
              <Route path="pix/limites" element={<PixLimitsPage />} />
              <Route path="seguranca" element={<SecurityPage />} />
              <Route path="notificacoes" element={<NotificationsPage />} />
            </Route>
          </Route>
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
