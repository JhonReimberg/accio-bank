// Shapes returned by the API (backend records serialized as camelCase JSON).

export type Role = 'Customer' | 'Analyst' | 'Admin'

export type Session = { accessToken: string; expiresAt: string; name: string; role: Role }

export type LoginResponse = { requiresTwoFactor: boolean; challengeToken: string | null; session: Session | null }

export type Paged<T> = { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number }

export type AccountStatus = 'PendingReview' | 'Active' | 'Blocked' | 'Closed'
export type AccountTier = 'Basic' | 'Plus' | 'Premium'

export type Account = {
  name: string
  email: string
  number: string
  status: AccountStatus
  tier: AccountTier
  availableBalance: number
  blockedBalance: number
  totalBalance: number
}

export type TransactionKind = 'Deposit' | 'InternalTransfer' | 'Pix' | 'PixRefund' | 'LoanDisbursement' | 'LoanPayment' | 'OverdraftInterest'

export type StatementItem = {
  transactionId: string
  kind: TransactionKind
  direction: 'Debit' | 'Credit'
  amount: number
  description: string
  counterparty: string | null
  balanceAfter: number
  createdAt: string
}

export type OnboardingStep = 'PersonalData' | 'Address' | 'Financial' | 'Documents' | 'TransactionPin' | 'Submit' | 'Done'
export type KycStatus = 'NotSubmitted' | 'PendingReview' | 'Approved' | 'Rejected'
export type DocumentType = 'IdentityDocument' | 'Selfie'

export type OnboardingStatus = {
  nextStep: OnboardingStep
  kycStatus: KycStatus
  accountStatus: AccountStatus
  accountTier: AccountTier
  rejectionReason: string | null
  fullName: string | null
  maskedCpf: string | null
  uploadedDocuments: DocumentType[]
}

export type CepAddress = { cep: string; street: string; neighborhood: string; city: string; state: string }

export type PixKeyType = 'Cpf' | 'Email' | 'Phone' | 'Random'

export type PixKey = { id: string; type: PixKeyType; value: string; createdAt: string }

export type PixRecipient = { name: string; maskedCpf: string | null; bank: string; keyType: PixKeyType; maskedKey: string }

export type PixTransferResult = {
  id: string
  endToEndId: string
  transactionId: string
  amount: number
  recipientName: string
  createdAt: string
  /** UnderReview: held by anti-fraud; id is the review id and there is no endToEndId yet. */
  status: 'Completed' | 'UnderReview'
}

export type PixTransferDetail = {
  id: string
  endToEndId: string
  amount: number
  refundedAmount: number
  refundableAmount: number
  description: string | null
  received: boolean
  canRefund: boolean
  createdAt: string
}

export type QrCode = { payload: string; qrCodePngBase64: string; maskedKey: string; amount: number | null }

export type DecodedBrCode = { amount: number | null; description: string | null; recipient: PixRecipient }

export type PixLimits = {
  daytimeLimit: number
  nighttimeLimit: number
  pendingDaytimeLimit: number | null
  pendingNighttimeLimit: number | null
  pendingEffectiveAt: string | null
  maxDaytimeLimit: number
  maxNighttimeLimit: number
  isNightNow: boolean
  usedInCurrentPeriod: number
  availableInCurrentPeriod: number
}

export type ScheduleFrequency = 'Once' | 'Weekly' | 'Monthly'

export type PixSchedule = {
  id: string
  recipientName: string
  keyType: PixKeyType
  maskedKey: string
  amount: number
  description: string | null
  frequency: ScheduleFrequency
  nextRunDate: string
  endDate: string | null
  status: 'Active' | 'Completed' | 'Cancelled'
  executions: number
  lastError: string | null
}

export type Contact = { id: string; nickname: string; keyType: PixKeyType; maskedKey: string; keyValue: string }

export type Notification = { id: string; title: string; body: string; createdAt: string; read: boolean }

export type SecurityOverview = { twoFactorEnabled: boolean; hasTransactionPin: boolean }

export type TwoFactorSetup = { secret: string; otpAuthUri: string; qrCodePngBase64: string }

export type Device = {
  id: string
  name: string
  trusted: boolean
  current: boolean
  firstSeenAt: string
  lastSeenAt: string
  lastIpAddress: string | null
}

export type LoginAttempt = { createdAt: string; succeeded: boolean; failureReason: string | null; ipAddress: string | null; device: string | null }

// ---------- Credit (phase 2) ----------

export type RuleResult = { code: string; passed: boolean; message: string; progress: number | null }

export type LoanOffer = {
  maxAmount: number
  monthlyRate: number
  minInstallments: number
  maxInstallments: number
  minAmount: number
  maxInstallmentAmount: number
}

export type Eligibility = { eligible: boolean; score: number | null; rules: RuleResult[]; offer: LoanOffer | null }

export type AmortizationRow = { number: number; dueDate: string; payment: number; interest: number; principal: number; balance: number }

export type LoanQuote = {
  requestedAmount: number
  installments: number
  monthlyRate: number
  installmentAmount: number
  iof: number
  netAmount: number
  totalInterest: number
  totalPaid: number
  cetMonthly: number
  cetYearly: number
  schedule: AmortizationRow[]
}

export type InstallmentStatus = 'Pending' | 'Overdue' | 'Paid'

export type Installment = {
  number: number
  dueDate: string
  amount: number
  interest: number
  principal: number
  balanceAfter: number
  status: InstallmentStatus
  paidAmount: number | null
  paidAt: string | null
  amountDueToday: number | null
}

export type Loan = {
  id: string
  amount: number
  netAmount: number
  iof: number
  monthlyRate: number
  cetMonthly: number
  cetYearly: number
  installmentCount: number
  installmentAmount: number
  status: 'Active' | 'PaidOff'
  outstandingPrincipal: number
  contractedAt: string
  installments: Installment[]
}

export type Overdraft = { limit: number; used: number; maxLimitOffered: number; monthlyRate: number }

export type ScoreFactor = { code: string; description: string; points: number }

export type Score = { score: number; factors: ScoreFactor[]; calculatedAt: string; history: { score: number; calculatedAt: string }[] }

// ---------- Back-office ----------

export type Dashboard = {
  customers: number
  activeAccounts: number
  pendingKyc: number
  pixVolume30Days: number
  pixCount30Days: number
  pixByDay: { day: string; amount: number; count: number }[]
  creditPortfolio: number
  activeLoans: number
  overdueAmount: number
  overdueInstallments: number
  delinquencyRate: number
  overdraftUsed: number
  ledgerBalanced: boolean
}

export type CustomerListItem = {
  userId: string
  name: string
  email: string
  maskedCpf: string | null
  accountStatus: AccountStatus | null
  tier: AccountTier | null
  kycStatus: KycStatus
  score: number | null
}

export type Customer360 = {
  userId: string
  accountId: string | null
  name: string
  email: string
  cpf: string | null
  phone: string | null
  birthDate: string | null
  address: { cep: string; street: string; number: string; complement: string | null; neighborhood: string; city: string; state: string } | null
  monthlyIncome: number | null
  occupation: string | null
  kycStatus: KycStatus
  createdAt: string
  account: {
    number: string
    status: AccountStatus
    statusReason: string | null
    tier: AccountTier
    balance: number
    blockedBalance: number
    overdraftLimit: number
    overdraftUsed: number
    activatedAt: string | null
  } | null
  score: Score | null
  loans: { id: string; amount: number; installmentCount: number; paidInstallments: number; overdueInstallments: number; outstandingPrincipal: number; status: string; contractedAt: string }[]
  recentTransactions: { transactionId: string; kind: TransactionKind; amount: number; description: string; createdAt: string }[]
}

export type KycQueueItem = {
  customerId: string
  fullName: string | null
  email: string
  maskedCpf: string | null
  monthlyIncome: number | null
  submittedAt: string | null
  status: KycStatus
}

export type KycDetail = {
  customerId: string
  fullName: string | null
  email: string
  cpf: string | null
  birthDate: string | null
  phone: string | null
  address: Customer360['address']
  monthlyIncome: number | null
  occupation: string | null
  status: KycStatus
  rejectionReason: string | null
  submittedAt: string | null
  documents: { id: string; type: DocumentType; contentType: string; sizeBytes: number; uploadedAt: string }[]
}

export type AuditLog = {
  id: string
  actorUserId: string | null
  action: string
  entityType: string
  entityId: string | null
  before: string | null
  after: string | null
  ipAddress: string | null
  createdAt: string
}

export type ScoreBand = { minScore: number; monthlyRate: number; preApprovedIncomeMultiplier: number; overdraftLimit: number }

export type CreditPolicy = {
  minAccountAgeDays: number
  minMovement30Days: number
  minScore: number
  maxIncomeCommitment: number
  minInstallments: number
  maxInstallments: number
  minLoanAmount: number
  lateFeeRate: number
  dailyLateInterestRate: number
  overdraftMonthlyRate: number
  cdiAnnualRate: number
  guaranteedMonthlyRate: number
  bands: ScoreBand[]
  updatedAt: string | null
}

// ---------- Phase 3: investments, cards, boletos, anti-fraud ----------

export type Investment = {
  id: string
  type: 'Cdb' | 'Caixinha'
  name: string
  goal: number | null
  cdiPercent: number
  principal: number
  value: number
  income: number
  blockedAmount: number
  availableToRedeem: number
  roundUpCardPurchases: boolean
  goalProgress: number | null
  createdAt: string
}

export type RedemptionPreview = { gross: number; income: number; iof: number; incomeTax: number; net: number }

export type BankCard = {
  id: string
  function: 'Debit' | 'Credit'
  form: 'Physical' | 'Virtual' | 'VirtualTemporary'
  masked: string
  holderName: string
  expiry: string
  status: 'Active' | 'Blocked' | 'Cancelled'
  onlinePurchasesEnabled: boolean
}

export type CardDetails = { number: string; expiry: string; holderName: string }

export type Purchase = { id: string; approved: boolean; declineReason: string | null; amount: number; installments: number; roundUp: number }

export type InvoiceItem = { type: 'Purchase' | 'PreviousBalance' | 'RevolvingInterest' | 'LateFee'; description: string; amount: number }

export type Invoice = {
  id: string
  closingDate: string
  dueDate: string
  status: 'Open' | 'Closed' | 'Paid' | 'Revolved'
  total: number
  paidAmount: number
  remaining: number
  minimumPayment: number
  items: InvoiceItem[]
}

export type CreditLine = { limit: number; used: number; available: number; dueDay: number; invoices: Invoice[] }

export type Boleto = {
  id: string
  amount: number
  dueDate: string
  description: string | null
  digitableLine: string
  barcode: string
  status: 'Open' | 'Paid' | 'Cancelled'
  createdAt: string
  paidAt: string | null
}

export type BoletoInfo = { barcode: string; amount: number; dueDate: string | null; beneficiaryName: string; issuedByVela: boolean; overdue: boolean }

export type BoletoPayment = {
  id: string
  beneficiaryName: string
  amount: number
  dueDate: string | null
  scheduledFor: string
  status: 'Scheduled' | 'Paid' | 'Failed' | 'Cancelled'
  failureReason: string | null
  createdAt: string
  paidAt: string | null
}

export type FraudReview = {
  id: string
  amount: number
  payeeName: string
  description: string | null
  reasons: string
  status: 'Pending' | 'Approved' | 'Rejected'
  createdAt: string
  reviewedAt: string | null
}

export type FraudPolicy = {
  enabled: boolean
  averageMultiplier: number
  averageRuleMinAmount: number
  newDeviceAmount: number
  velocityCount: number
}
