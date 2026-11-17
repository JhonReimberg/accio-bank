import { useState, type SyntheticEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Alert, Button, Card, Field, PageHeader, PinDialog, Select } from '../../components/ui'
import { api, download } from '../../lib/api'
import { brl, dateTime, keyTypeLabel, parseAmount, todayInBrasilia } from '../../lib/format'
import { keys, useMoneyMoved } from '../../lib/queries'
import type { Contact, DecodedBrCode, PixRecipient, PixTransferResult, ScheduleFrequency } from '../../lib/types'

type Target =
  | { mode: 'key'; key: string; recipient: PixRecipient }
  | { mode: 'brcode'; brCode: string; recipient: PixRecipient; fixedAmount: number | null; description: string | null }

export default function PixSendPage() {
  const [target, setTarget] = useState<Target | null>(null)
  const [result, setResult] = useState<PixTransferResult | string | null>(null)

  return (
    <div className="stack-lg">
      <PageHeader title="Pagar com PIX" back="/pix" />
      {result ? (
        <Done result={result} target={target!} onAgain={() => (setResult(null), setTarget(null))} />
      ) : target ? (
        <Confirm target={target} onBack={() => setTarget(null)} onDone={setResult} />
      ) : (
        <ChooseTarget onChosen={setTarget} />
      )}
    </div>
  )
}

function ChooseTarget({ onChosen }: { onChosen: (t: Target) => void }) {
  const [mode, setMode] = useState<'key' | 'brcode'>('key')
  const [input, setInput] = useState('')
  const contacts = useQuery({ queryKey: [...keys.pix, 'contacts'], queryFn: () => api<Contact[]>('/pix/contacts') })
  const lookup = useMutation({
    mutationFn: async (value: string): Promise<Target> => {
      if (mode === 'key') {
        const recipient = await api<PixRecipient>(`/pix/keys/lookup?key=${encodeURIComponent(value)}`)
        return { mode: 'key', key: value, recipient }
      }
      const decoded = await api<DecodedBrCode>('/pix/brcode/decode', { body: { payload: value } })
      return { mode: 'brcode', brCode: value, recipient: decoded.recipient, fixedAmount: decoded.amount, description: decoded.description }
    },
    onSuccess: onChosen,
  })

  function submit(e: SyntheticEvent) {
    e.preventDefault()
    lookup.mutate(input.trim())
  }

  return (
    <Card>
      <div className="tabs" role="tablist">
        {(['key', 'brcode'] as const).map((m) => (
          <button key={m} role="tab" aria-selected={mode === m} className={mode === m ? 'tab-active' : ''} onClick={() => (setMode(m), setInput(''))}>
            {m === 'key' ? 'Chave PIX' : 'Copia e cola'}
          </button>
        ))}
      </div>
      <form className="stack" onSubmit={submit}>
        {mode === 'key' ? (
          <Field
            label="Chave do destinatário"
            hint="CPF, e-mail, celular com +55 ou chave aleatória."
            required
            maxLength={100}
            value={input}
            onChange={(e) => setInput(e.target.value)}
          />
        ) : (
          <div className="field">
            <label htmlFor="brcode">Código copia e cola</label>
            <textarea id="brcode" rows={4} required value={input} onChange={(e) => setInput(e.target.value)} />
          </div>
        )}
        <Alert kind="error">{lookup.error?.message}</Alert>
        <Button busy={lookup.isPending}>Continuar</Button>
      </form>
      {mode === 'key' && contacts.data && contacts.data.length > 0 && (
        <div className="stack">
          <h3>Favoritos</h3>
          <div className="chips">
            {contacts.data.map((c) => (
              <button key={c.id} className="chip" onClick={() => lookup.mutate(c.keyValue)}>
                {c.nickname} <span className="muted small">{c.maskedKey}</span>
              </button>
            ))}
          </div>
        </div>
      )}
    </Card>
  )
}

function Confirm({ target, onBack, onDone }: { target: Target; onBack: () => void; onDone: (r: PixTransferResult | string) => void }) {
  const moneyMoved = useMoneyMoved()
  const fixed = target.mode === 'brcode' ? target.fixedAmount : null
  const [amount, setAmount] = useState(fixed ? String(fixed).replace('.', ',') : '')
  const [message, setMessage] = useState(target.mode === 'brcode' ? (target.description ?? '') : '')
  const [schedule, setSchedule] = useState(false)
  const [firstDate, setFirstDate] = useState(todayInBrasilia())
  const [frequency, setFrequency] = useState<ScheduleFrequency>('Once')
  const [asking, setAsking] = useState(false)
  const value = parseAmount(amount)

  const pay = useMutation({
    mutationFn: async (pin: string): Promise<PixTransferResult | string> => {
      if (schedule && target.mode === 'key') {
        await api('/pix/schedules', {
          body: { key: target.key, amount: value, description: message || null, firstDate, frequency, endDate: null, pin },
          idempotent: true,
        })
        return frequency === 'Once' ? 'PIX agendado.' : 'PIX recorrente criado.'
      }
      return api<PixTransferResult>('/pix/transfers', {
        body: {
          key: target.mode === 'key' ? target.key : null,
          brCode: target.mode === 'brcode' ? target.brCode : null,
          amount: fixed ?? value,
          description: message || null,
          pin,
        },
        idempotent: true,
      })
    },
    onSuccess: (r) => {
      setAsking(false)
      moneyMoved()
      onDone(r)
    },
  })

  const r = target.recipient
  return (
    <Card className="stack">
      <div className="recipient">
        <p className="muted small">Para</p>
        <p className="big">{r.name}</p>
        <p className="muted small">
          {r.maskedCpf && `CPF ${r.maskedCpf} · `}
          {r.bank} · {keyTypeLabel[r.keyType]} {r.maskedKey}
        </p>
      </div>
      <form
        className="stack"
        onSubmit={(e) => {
          e.preventDefault()
          pay.reset()
          setAsking(true)
        }}
      >
        <Field
          label="Valor (R$)"
          inputMode="decimal"
          required
          readOnly={fixed !== null}
          hint={fixed !== null ? 'Valor definido no código PIX.' : undefined}
          value={amount}
          onChange={(e) => setAmount(e.target.value)}
          placeholder="0,00"
        />
        <Field label="Mensagem (opcional)" maxLength={140} value={message} onChange={(e) => setMessage(e.target.value)} />
        {target.mode === 'key' && (
          <label className="check">
            <input type="checkbox" checked={schedule} onChange={(e) => setSchedule(e.target.checked)} /> Agendar ou repetir
          </label>
        )}
        {schedule && (
          <div className="grid-2">
            <Field label="Data" type="date" min={todayInBrasilia()} required value={firstDate} onChange={(e) => setFirstDate(e.target.value)} />
            <Select label="Repetir" value={frequency} onChange={(e) => setFrequency(e.target.value as ScheduleFrequency)}>
              <option value="Once">Não repetir</option>
              <option value="Weekly">Toda semana</option>
              <option value="Monthly">Todo mês</option>
            </Select>
          </div>
        )}
        <div className="row">
          <Button type="button" variant="ghost" onClick={onBack}>
            Voltar
          </Button>
          <Button disabled={Number.isNaN(value) && fixed === null}>
            {schedule ? 'Agendar' : `Pagar ${Number.isNaN(value) ? '' : brl(fixed ?? value)}`}
          </Button>
        </div>
      </form>
      <PinDialog open={asking} busy={pay.isPending} error={pay.error?.message} onConfirm={(pin) => pay.mutate(pin)} onCancel={() => setAsking(false)} />
    </Card>
  )
}

function Done({ result, target, onAgain }: { result: PixTransferResult | string; target: Target; onAgain: () => void }) {
  const client = useQueryClient()
  const [nickname, setNickname] = useState(target.recipient.name.split(' ')[0])
  const save = useMutation({
    mutationFn: () => api('/pix/contacts', { body: { nickname, key: target.mode === 'key' ? target.key : '' } }),
    onSuccess: () => client.invalidateQueries({ queryKey: [...keys.pix, 'contacts'] }),
  })

  if (typeof result !== 'string' && result.status === 'UnderReview')
    return (
      <Card className="stack center">
        <p className="success-icon" aria-hidden>
          ⏳
        </p>
        <h2>PIX em análise</h2>
        <p className="big">{brl(result.amount)}</p>
        <p>para {result.recipientName}</p>
        <p className="muted">
          Por segurança, este PIX vai passar por uma análise rápida. O valor fica reservado na sua conta e avisamos assim que ele for liberado
          ou recusado.
        </p>
        <Button variant="ghost" onClick={onAgain}>
          Voltar ao PIX
        </Button>
      </Card>
    )

  if (typeof result === 'string')
    return (
      <Card className="stack center">
        <h2>{result}</h2>
        <p className="muted">Ele é executado automaticamente na data escolhida.</p>
        <Link to="/pix/agendados">Ver agendamentos</Link>
        <Button variant="ghost" onClick={onAgain}>
          Fazer outro PIX
        </Button>
      </Card>
    )

  return (
    <Card className="stack center">
      <p className="success-icon" aria-hidden>
        ✓
      </p>
      <h2>PIX enviado</h2>
      <p className="big">{brl(result.amount)}</p>
      <p>
        para {result.recipientName} · {dateTime(result.createdAt)}
      </p>
      <p className="muted small mono">{result.endToEndId}</p>
      <Button variant="ghost" onClick={() => download(`/account/statement/${result.transactionId}/receipt`, `pix-${result.endToEndId}.pdf`)}>
        Baixar comprovante
      </Button>
      {target.mode === 'key' && !save.isSuccess && (
        <form
          className="row"
          onSubmit={(e) => {
            e.preventDefault()
            save.mutate()
          }}
        >
          <Field label="Salvar nos favoritos como" value={nickname} maxLength={50} onChange={(e) => setNickname(e.target.value)} />
          <Button variant="ghost" busy={save.isPending}>
            Salvar
          </Button>
        </form>
      )}
      <Alert kind="success">{save.isSuccess ? 'Contato salvo.' : ''}</Alert>
      <Alert kind="error">{save.error?.message}</Alert>
      <Button onClick={onAgain}>Fazer outro PIX</Button>
    </Card>
  )
}
