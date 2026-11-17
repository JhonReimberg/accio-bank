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

export type TransactionKind = 'Deposit' | 'InternalTransfer' | 'Pix' | 'PixRefund'

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
