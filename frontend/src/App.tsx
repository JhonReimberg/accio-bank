import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router'
import Layout from './components/Layout'
import { ErrorBox, Loading } from './components/ui'
import AuditPage from './pages/backoffice/AuditPage'
import BackofficeLayout from './pages/backoffice/BackofficeLayout'
import { CustomerPage, CustomersPage } from './pages/backoffice/CustomersPage'
import DashboardPage from './pages/backoffice/DashboardPage'
import FraudPage from './pages/backoffice/FraudPage'
import KycQueuePage from './pages/backoffice/KycQueuePage'
import PolicyPage from './pages/backoffice/PolicyPage'
import CardsPage, { CardDetailPage, IssueCardPage, OpenCreditLinePage, SimulatePurchasePage } from './pages/cards/CardsPage'
import InvoicePage from './pages/cards/InvoicePage'
import CreditPage, { ScorePage, SimulatePage } from './pages/credit/CreditPage'
import InvestmentsPage, { InvestmentDetailPage, InvestmentOperationPage, NewInvestmentPage } from './pages/investments/InvestmentsPage'
import PaymentsPage, { IssueBoletoPage, PayBoletoPage, RechargePage } from './pages/payments/PaymentsPage'
import LoansPage from './pages/credit/LoansPage'
import OverdraftPage from './pages/credit/OverdraftPage'
import { useAuth } from './lib/useAuth'
import { useOnboarding } from './lib/queries'
import { LoginPage, RegisterPage } from './pages/AuthPages'
import AccountPage from './pages/AccountPage'
import DepositPage from './pages/DepositPage'
import { BenefitsPage, HelpPage, MenuPage, ProfilePage, SettingsPage } from './pages/MenuPages'
import DesignSystemPage from './pages/DesignSystemPage'
import HomePage from './pages/HomePage'
import NotificationsPage from './pages/NotificationsPage'
import OnboardingPage from './pages/OnboardingPage'
import PixHomePage from './pages/pix/PixHomePage'
import PixKeysPage from './pages/pix/PixKeysPage'
import PixLimitsPage from './pages/pix/PixLimitsPage'
import PixReceivePage from './pages/pix/PixReceivePage'
import PixSchedulesPage from './pages/pix/PixSchedulesPage'
import PixSendPage from './pages/pix/PixSendPage'
import SecurityPage, { AccessHistoryPage, DevicesPage, PinPage, TwoFactorPage } from './pages/SecurityPage'
import StatementPage from './pages/StatementPage'

function PublicOnly() {
  const { state } = useAuth()
  if (state.status === 'loading') return <Loading />
  return state.status === 'authenticated' ? <Navigate to="/" replace /> : <Outlet />
}

function SignedIn() {
  const { state } = useAuth()
  if (state.status === 'loading') return <Loading />
  if (state.status === 'anonymous') return <Navigate to="/entrar" replace />
  return <Outlet />
}

/** Analysts and admins work in the back-office; customers in the banking app. Each is sent to its own area. */
function RoleArea({ staff }: { staff: boolean }) {
  const { state } = useAuth()
  const isStaff = state.status === 'authenticated' && state.session.role !== 'Customer'
  if (isStaff !== staff) return <Navigate to={isStaff ? '/backoffice' : '/'} replace />
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
        <Route path="/design-system" element={<DesignSystemPage />} />
        <Route element={<PublicOnly />}>
          <Route path="/entrar" element={<LoginPage />} />
          <Route path="/abrir-conta" element={<RegisterPage />} />
        </Route>
        <Route element={<SignedIn />}>
          <Route element={<RoleArea staff />}>
            <Route path="/backoffice" element={<BackofficeLayout />}>
              <Route index element={<DashboardPage />} />
              <Route path="kyc" element={<KycQueuePage />} />
              <Route path="antifraude" element={<FraudPage />} />
              <Route path="clientes" element={<CustomersPage />} />
              <Route path="clientes/:userId" element={<CustomerPage />} />
              <Route path="auditoria" element={<AuditPage />} />
              <Route path="politica" element={<PolicyPage />} />
            </Route>
          </Route>
          <Route element={<RoleArea staff={false} />}>
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
              <Route path="seguranca/duas-etapas" element={<TwoFactorPage />} />
              <Route path="seguranca/senha" element={<PinPage />} />
              <Route path="seguranca/dispositivos" element={<DevicesPage />} />
              <Route path="seguranca/acessos" element={<AccessHistoryPage />} />
              <Route path="conta" element={<AccountPage />} />
              <Route path="menu" element={<MenuPage />} />
              <Route path="perfil" element={<ProfilePage />} />
              <Route path="configurar" element={<SettingsPage />} />
              <Route path="ajuda" element={<HelpPage />} />
              <Route path="beneficios" element={<BenefitsPage />} />
              <Route path="recarga" element={<RechargePage />} />
              <Route path="notificacoes" element={<NotificationsPage />} />
              <Route path="credito" element={<CreditPage />} />
              <Route path="credito/emprestimos" element={<LoansPage />} />
              <Route path="credito/cheque-especial" element={<OverdraftPage />} />
              <Route path="credito/simular" element={<SimulatePage />} />
              <Route path="credito/score" element={<ScorePage />} />
              <Route path="cartoes" element={<CardsPage />} />
              <Route path="cartoes/fatura" element={<InvoicePage />} />
              <Route path="cartoes/novo" element={<IssueCardPage />} />
              <Route path="cartoes/ativar-credito" element={<OpenCreditLinePage />} />
              <Route path="cartoes/:id" element={<CardDetailPage />} />
              <Route path="cartoes/:id/simular" element={<SimulatePurchasePage />} />
              <Route path="investimentos" element={<InvestmentsPage />} />
              <Route path="investimentos/novo" element={<NewInvestmentPage />} />
              <Route path="investimentos/:id" element={<InvestmentDetailPage />} />
              <Route path="investimentos/:id/aplicar" element={<InvestmentOperationPage kind="applications" />} />
              <Route path="investimentos/:id/resgatar" element={<InvestmentOperationPage kind="redemptions" />} />
              <Route path="investimentos/:id/garantia" element={<InvestmentOperationPage kind="loan" />} />
              <Route path="pagamentos" element={<PaymentsPage />} />
              <Route path="pagamentos/boleto" element={<PayBoletoPage />} />
              <Route path="pagamentos/cobrar" element={<IssueBoletoPage />} />
            </Route>
          </Route>
          </Route>
        </Route>
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </BrowserRouter>
  )
}
