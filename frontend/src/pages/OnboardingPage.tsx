import { useState, type SyntheticEvent, type ReactNode } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Navigate } from 'react-router'
import { Alert, Button, Card, ErrorBox, Field, Loading } from '../components/ui'
import { api } from '../lib/api'
import { useAuth } from '../lib/useAuth'
import { maskCep, maskCpf, maskPhone, parseAmount } from '../lib/format'
import { keys, useOnboarding } from '../lib/queries'
import type { CepAddress, DocumentType, OnboardingStatus, OnboardingStep } from '../lib/types'

const steps: { id: Exclude<OnboardingStep, 'Done'>; label: string }[] = [
  { id: 'PersonalData', label: 'Dados pessoais' },
  { id: 'Address', label: 'Endereço' },
  { id: 'Financial', label: 'Renda' },
  { id: 'Documents', label: 'Documentos' },
  { id: 'TransactionPin', label: 'Senha transacional' },
  { id: 'Submit', label: 'Revisão' },
]

export default function OnboardingPage() {
  const { logout } = useAuth()
  const status = useOnboarding()
  const [chosen, setChosen] = useState<OnboardingStep | null>(null)

  if (status.isPending) return <Loading />
  if (status.isError) return <ErrorBox error={status.error} retry={status.refetch} />
  const data = status.data
  if (data.nextStep === 'Done' && data.accountStatus === 'Active') return <Navigate to="/" replace />

  const current = data.nextStep === 'Done' ? 'Done' : (chosen ?? data.nextStep)
  const reached = steps.findIndex((s) => s.id === data.nextStep)

  return (
    <main className="onboarding">
      <header className="page-header">
        <h1>Abertura de conta</h1>
        <div className="page-actions">
          <Button variant="ghost" onClick={logout}>
            Sair
          </Button>
        </div>
      </header>

      {data.nextStep === 'Done' ? (
        <Card className="stack center">
          <h2>Cadastro em análise</h2>
          <p className="muted">Recebemos seus dados e documentos. Avisaremos por notificação e e-mail assim que a conta for aprovada.</p>
        </Card>
      ) : (
        <>
          <ol className="stepper">
            {steps.map((s, i) => (
              <li key={s.id} className={s.id === current ? 'active' : i < reached || data.nextStep === 'Submit' ? 'done' : ''}>
                <button type="button" disabled={i > reached && data.nextStep !== 'Submit'} onClick={() => setChosen(s.id)}>
                  <span className="step-number">{i + 1}</span>
                  <span className="step-label">{s.label}</span>
                </button>
              </li>
            ))}
          </ol>
          {data.kycStatus === 'Rejected' && (
            <Alert kind="error">Seu cadastro precisa de ajustes: {data.rejectionReason}. Corrija e envie novamente.</Alert>
          )}
          <Card>
            <Step step={current} status={data} onSaved={() => setChosen(null)} />
          </Card>
        </>
      )}
    </main>
  )
}

function Step({ step, status, onSaved }: { step: OnboardingStep; status: OnboardingStatus; onSaved: () => void }) {
  switch (step) {
    case 'PersonalData':
      return <PersonalDataStep onSaved={onSaved} />
    case 'Address':
      return <AddressStep onSaved={onSaved} />
    case 'Financial':
      return <FinancialStep onSaved={onSaved} />
    case 'Documents':
      return <DocumentsStep status={status} onSaved={onSaved} />
    case 'TransactionPin':
      return <PinStep onSaved={onSaved} />
    default:
      return <SubmitStep status={status} />
  }
}

/** Shared save logic: call the API, refresh the onboarding status (which moves to the next step). */
function useStepSave(onSaved: () => void) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (save: () => Promise<unknown>) => save(),
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: keys.onboarding })
      onSaved()
    },
  })
}

function StepForm({ title, intro, onSubmit, error, busy, children, submitLabel = 'Salvar e continuar' }: {
  title: string
  intro?: string
  onSubmit: (form: FormData) => void
  error?: string
  busy: boolean
  children: ReactNode
  submitLabel?: string
}) {
  function submit(e: SyntheticEvent<HTMLFormElement>) {
    e.preventDefault()
    onSubmit(new FormData(e.currentTarget))
  }
  return (
    <form className="stack" onSubmit={submit}>
      <h2>{title}</h2>
      {intro && <p className="muted">{intro}</p>}
      {children}
      <Alert kind="error">{error}</Alert>
      <Button busy={busy}>{submitLabel}</Button>
    </form>
  )
}

function PersonalDataStep({ onSaved }: { onSaved: () => void }) {
  const save = useStepSave(onSaved)
  const [cpf, setCpf] = useState('')
  const [phone, setPhone] = useState('')
  return (
    <StepForm
      title="Dados pessoais"
      intro="Precisamos do seu nome completo e CPF, como no documento."
      busy={save.isPending}
      error={save.error?.message}
      onSubmit={(f) =>
        save.mutate(() =>
          api('/onboarding/personal-data', {
            method: 'PUT',
            body: { fullName: f.get('fullName'), cpf, birthDate: f.get('birthDate'), phone },
          }),
        )
      }
    >
      <Field label="Nome completo" name="fullName" autoComplete="name" required minLength={5} maxLength={100} />
      <Field label="CPF" inputMode="numeric" required value={cpf} onChange={(e) => setCpf(maskCpf(e.target.value))} placeholder="000.000.000-00" />
      <Field label="Data de nascimento" name="birthDate" type="date" required />
      <Field label="Celular" inputMode="tel" autoComplete="tel-national" required value={phone} onChange={(e) => setPhone(maskPhone(e.target.value))} placeholder="(11) 91234-5678" />
    </StepForm>
  )
}

function AddressStep({ onSaved }: { onSaved: () => void }) {
  const save = useStepSave(onSaved)
  const [address, setAddress] = useState({ cep: '', street: '', number: '', complement: '', neighborhood: '', city: '', state: '' })
  const [lookup, setLookup] = useState('')
  const set = (field: keyof typeof address) => (e: { target: { value: string } }) => setAddress((a) => ({ ...a, [field]: e.target.value }))

  async function changeCep(value: string) {
    const cep = maskCep(value)
    setAddress((a) => ({ ...a, cep }))
    if (cep.replace(/\D/g, '').length !== 8) return
    setLookup('Buscando endereço…')
    try {
      const found = await api<CepAddress>(`/onboarding/cep/${cep}`)
      setAddress((a) => ({ ...a, street: found.street, neighborhood: found.neighborhood, city: found.city, state: found.state }))
      setLookup('')
    } catch (err) {
      setLookup((err as Error).message)
    }
  }

  return (
    <StepForm
      title="Endereço"
      intro="Digite o CEP e completamos o resto."
      busy={save.isPending}
      error={save.error?.message}
      onSubmit={() => save.mutate(() => api('/onboarding/address', { method: 'PUT', body: address }))}
    >
      <Field label="CEP" inputMode="numeric" autoComplete="postal-code" required value={address.cep} onChange={(e) => changeCep(e.target.value)} hint={lookup} />
      <Field label="Rua" required value={address.street} onChange={set('street')} />
      <div className="grid-2">
        <Field label="Número" required value={address.number} onChange={set('number')} />
        <Field label="Complemento" value={address.complement} onChange={set('complement')} />
      </div>
      <Field label="Bairro" required value={address.neighborhood} onChange={set('neighborhood')} />
      <div className="grid-2">
        <Field label="Cidade" required value={address.city} onChange={set('city')} />
        <Field label="UF" required maxLength={2} value={address.state} onChange={set('state')} />
      </div>
    </StepForm>
  )
}

function FinancialStep({ onSaved }: { onSaved: () => void }) {
  const save = useStepSave(onSaved)
  const [error, setError] = useState('')
  return (
    <StepForm
      title="Renda e profissão"
      intro="A renda declarada define o nível da sua conta (Basic, Plus ou Premium) e seus limites."
      busy={save.isPending}
      error={error || save.error?.message}
      onSubmit={(f) => {
        const income = parseAmount(String(f.get('income')))
        if (Number.isNaN(income)) return setError('Informe a renda em reais, por exemplo 3500,00.')
        setError('')
        save.mutate(() => api('/onboarding/financial-profile', { method: 'PUT', body: { monthlyIncome: income, occupation: f.get('occupation') } }))
      }}
    >
      <Field label="Renda mensal (R$)" name="income" inputMode="decimal" required placeholder="3.500,00" />
      <Field label="Profissão" name="occupation" required maxLength={100} />
    </StepForm>
  )
}

function DocumentsStep({ status, onSaved }: { status: OnboardingStatus; onSaved: () => void }) {
  const client = useQueryClient()
  const upload = useMutation({
    mutationFn: ({ type, file }: { type: DocumentType; file: File }) => {
      const form = new FormData()
      form.append('file', file)
      return api(`/onboarding/documents/${type}`, { form })
    },
    onSuccess: () => client.invalidateQueries({ queryKey: keys.onboarding }),
  })
  const done = (type: DocumentType) => status.uploadedDocuments.includes(type)
  const rows: { type: DocumentType; label: string; accept: string; hint: string }[] = [
    { type: 'IdentityDocument', label: 'Documento com foto (RG ou CNH)', accept: 'image/jpeg,image/png,application/pdf', hint: 'JPG, PNG ou PDF até 5 MB.' },
    { type: 'Selfie', label: 'Selfie segurando o documento', accept: 'image/jpeg,image/png', hint: 'JPG ou PNG até 5 MB.' },
  ]

  return (
    <div className="stack">
      <h2>Documentos</h2>
      <p className="muted">Os arquivos são enviados com segurança e só a equipe de análise tem acesso. Use imagens de teste: este banco é fictício.</p>
      {rows.map((r) => (
        <label key={r.type} className={`upload ${done(r.type) ? 'upload-done' : ''}`}>
          <span>
            <strong>{r.label}</strong>
            <small className="muted">{done(r.type) ? 'Enviado ✓ (envie de novo para substituir)' : r.hint}</small>
          </span>
          <input
            type="file"
            accept={r.accept}
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) upload.mutate({ type: r.type, file })
              e.target.value = ''
            }}
          />
        </label>
      ))}
      {upload.isPending && <p className="muted">Enviando…</p>}
      <Alert kind="error">{upload.error?.message}</Alert>
      <Button disabled={!done('IdentityDocument') || !done('Selfie')} onClick={onSaved}>
        Continuar
      </Button>
    </div>
  )
}

function PinStep({ onSaved }: { onSaved: () => void }) {
  const save = useStepSave(onSaved)
  const [error, setError] = useState('')
  return (
    <StepForm
      title="Senha transacional"
      intro="Uma senha de 4 dígitos, diferente da senha de acesso, pedida em todo PIX e pagamento. Evite sequências (1234) e números repetidos."
      busy={save.isPending}
      error={error || save.error?.message}
      onSubmit={(f) => {
        if (f.get('pin') !== f.get('confirm')) return setError('As senhas transacionais não conferem.')
        setError('')
        save.mutate(() => api('/security/pin', { method: 'PUT', body: { password: f.get('password'), pin: f.get('pin') } }))
      }}
    >
      <Field label="Senha de acesso (para confirmar que é você)" name="password" type="password" autoComplete="current-password" required />
      <div className="grid-2">
        <Field label="Senha transacional" name="pin" type="password" inputMode="numeric" maxLength={4} pattern="[0-9]{4}" required />
        <Field label="Repita" name="confirm" type="password" inputMode="numeric" maxLength={4} pattern="[0-9]{4}" required />
      </div>
    </StepForm>
  )
}

function SubmitStep({ status }: { status: OnboardingStatus }) {
  const save = useStepSave(() => undefined)
  const client = useQueryClient()
  return (
    <div className="stack">
      <h2>Tudo pronto</h2>
      <dl className="summary">
        <dt>Nome</dt>
        <dd>{status.fullName}</dd>
        <dt>CPF</dt>
        <dd>{status.maskedCpf}</dd>
        <dt>Documentos</dt>
        <dd>{status.uploadedDocuments.length} de 2 enviados</dd>
      </dl>
      <p className="muted">Ao enviar, seus dados passam por análise. Depois disso não é possível alterá-los pelo app.</p>
      <Alert kind="error">{save.error?.message}</Alert>
      <Button
        busy={save.isPending}
        onClick={() =>
          save.mutate(async () => {
            await api('/onboarding/submit', { method: 'POST' })
            await client.invalidateQueries({ queryKey: keys.account })
          })
        }
      >
        Enviar para análise
      </Button>
    </div>
  )
}
