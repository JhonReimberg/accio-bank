import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { Amount, Carousel, CircleActions, ErrorBox, Loading, Section } from '../components/ui'
import { Mascot } from '../components/Mascot'
import { api } from '../lib/api'
import { brl, dayLabel } from '../lib/format'
import { useHidden } from '../lib/hideBalance'
import { keys, useAccount } from '../lib/queries'
import type { CreditLine, Eligibility, Investment, Paged, StatementItem } from '../lib/types'

const actions = [
  { to: '/pix', icon: '◈', label: 'Área PIX e Transferir' },
  { to: '/depositar', icon: '↓', label: 'Trazer dinheiro' },
  { to: '/pagamentos/boleto', icon: '▥', label: 'Pagar' },
  { to: '/credito', icon: '◔', label: 'Empréstimos' },
  { to: '/recarga', icon: '▯', label: 'Recarga de celular' },
  { to: '/pagamentos/cobrar', icon: '↙', label: 'Cobrar' },
  { to: '/investimentos/novo', icon: '↗', label: 'Investir' },
  { to: '/conta?aba=guardar', icon: '◫', label: 'Cofres' },
]

const discover = [
  { to: '/beneficios', title: 'Benefícios Accio', text: 'Cashback e cupons para clientes.', cta: 'Conhecer' },
  { to: '/credito/score', title: 'Seu score', text: 'Veja o que aumenta seu score de crédito.', cta: 'Ver score' },
  { to: '/credito/cheque-especial', title: 'Cheque especial', text: 'Limite extra, juros só nos dias de uso.', cta: 'Ver limite' },
]

export default function HomePage() {
  const account = useAccount()
  const hidden = useHidden()
  const latest = useQuery({ queryKey: [...keys.statement, 'latest'], queryFn: () => api<Paged<StatementItem>>('/account/statement?pageSize=3') })
  const line = useQuery({ queryKey: keys.creditLine, queryFn: async () => (await api<CreditLine | undefined>('/cards/credit-line')) ?? null })
  const eligibility = useQuery({ queryKey: [...keys.account, 'eligibility'], queryFn: () => api<Eligibility>('/credit/eligibility') })
  const investments = useQuery({ queryKey: [...keys.account, 'investments'], queryFn: () => api<Investment[]>('/investments') })

  if (account.isPending) return <Loading />
  if (account.isError) return <ErrorBox error={account.error} retry={account.refetch} />
  const a = account.data
  const money = (v: number) => (hidden ? 'R$ ••••' : brl(v))
  const invested = investments.data?.reduce((s, i) => s + i.value, 0) ?? 0
  const vaults = investments.data?.filter((i) => i.type === 'Caixinha').reduce((s, i) => s + i.value, 0) ?? 0
  const invoice = line.data?.invoices.find((i) => i.status === 'Open' || i.status === 'Closed')
  const offer = eligibility.data?.eligible ? eligibility.data.offer : null

  return (
    <>
    <h1 className="home-greet desktop-only">Olá, {a.name.split(' ')[0]}</h1>
    <div className="home-nu">
      <div className="col col-a">
        <Section title="Saldo em conta" to="/conta">
          <p className="home-value">{money(a.availableBalance)}</p>
          <div className="pills">
            {a.blockedBalance > 0 && <span className="pill">Bloqueado {money(a.blockedBalance)}</span>}
            <Link to="/conta?aba=guardar" className="pill">
              ◫ Cofres {money(vaults)}
            </Link>
            <span className="pill">Conta {a.number}</span>
          </div>
        </Section>

        <CircleActions label="Ações rápidas" items={actions} />

        <div className="mobile-only">
          <Banners />
        </div>

        <Link to="/cartoes" className="wide-pill">
          <span aria-hidden>▭</span> Meus cartões
        </Link>

        <Section title="Cartão de crédito" to={line.data ? '/cartoes/fatura' : '/cartoes'}>
          {line.data ? (
            <>
              <p className="muted">Fatura atual</p>
              <p className="home-value small-value">{money(invoice?.remaining ?? 0)}</p>
              <p className="muted small">
                Limite disponível de {money(line.data.available)}
                {invoice && ` · vence ${invoice.dueDate.split('-').reverse().slice(0, 2).join('/')}`}
              </p>
            </>
          ) : (
            <p className="muted">Ative seu cartão de crédito sem anuidade.</p>
          )}
        </Section>

        <Section title="Empréstimo" to="/credito">
          <p className="muted">
            {offer ? (
              <>
                Valor disponível de até <strong className="text-strong">{money(offer.maxAmount)}</strong>
              </>
            ) : (
              'Veja o que falta para liberar seu empréstimo.'
            )}
          </p>
        </Section>

        <Section title="Investimentos" to="/investimentos">
          <p className="muted">Total investido</p>
          <p className="home-value small-value">{money(invested)}</p>
        </Section>

        <Section title="Últimas movimentações" to="/conta">
          {latest.isPending ? (
            <Loading />
          ) : latest.isError ? (
            <ErrorBox error={latest.error} retry={latest.refetch} />
          ) : latest.data.items.length === 0 ? (
            <p className="muted">Nenhuma movimentação ainda.</p>
          ) : (
            <ul className="list">
              {latest.data.items.map((t) => (
                <li key={t.transactionId + t.direction}>
                  <div className="tx">
                    <span className={`tx-icon ${t.amount < 0 ? 'out' : ''}`} aria-hidden>
                      {t.amount < 0 ? '↗' : '↙'}
                    </span>
                    <div>
                      <p>{t.description}</p>
                      <p className="muted small">{dayLabel(t.createdAt)}</p>
                    </div>
                  </div>
                  <Amount value={t.amount} hidden={hidden} />
                </li>
              ))}
            </ul>
          )}
        </Section>
      </div>

      <div className="col col-b">
        <div className="desktop-only">
          <Banners />
        </div>
        <section className="home-section">
          <h2 className="home-section-title">Descubra mais</h2>
          <div className="discover">
            {discover.map((d) => (
              <article key={d.to} className="discover-card">
                <div className="discover-art" aria-hidden>
                  <Mascot mood="think" size={64} />
                </div>
                <h3>{d.title}</h3>
                <p className="muted small">{d.text}</p>
                <Link to={d.to} className="btn btn-primary">
                  {d.cta}
                </Link>
              </article>
            ))}
          </div>
        </section>
        <Link to="/ajuda" className="rate-link">
          ♡ Avalie esta tela
        </Link>
      </div>
    </div>
    </>
  )
}

function Banners() {
  return (
    <Carousel label="Novidades">
      <Link to="/investimentos" className="banner">
        <span>
          <strong>Recado da coruja</strong>
          <span>Dinheiro parado não rende. Guarde num cofre e veja crescer como mágica.</span>
        </span>
        <Mascot mood="hello" size={54} />
      </Link>
      <Link to="/seguranca/duas-etapas" className="banner">
        <span>
          <strong>Feitiço de proteção</strong>
          <span>Ative a verificação em duas etapas e blinde sua conta.</span>
        </span>
        <span className="banner-icon" aria-hidden>
          ⚿
        </span>
      </Link>
      <Link to="/beneficios" className="banner">
        <span>
          <strong>Benefícios Accio</strong>
          <span>Cashback, cupons e sorteios em breve para clientes.</span>
        </span>
        <span className="banner-icon" aria-hidden>
          ✦
        </span>
      </Link>
    </Carousel>
  )
}
