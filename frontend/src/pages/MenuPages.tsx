import { Link } from 'react-router'
import { Alert, AvatarFace, ListRow, PageHeader, RowGroup } from '../components/ui'
import { Mascot } from '../components/Mascot'
import { usePhotoActions, useAvatar } from '../lib/avatar'
import { useAccount, useUnreadCount } from '../lib/queries'
import { useAuth } from '../lib/useAuth'

/** Full menu (☰): every product has its own screen. */
export function MenuPage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Menu" back="/" />
      <RowGroup>
        <ListRow to="/credito" icon="◔" title="Empréstimos" />
        <ListRow to="/conta?aba=guardar" icon="◫" title="Cofres" />
        <ListRow to="/investimentos" icon="▮" title="Investimentos" />
        <ListRow to="/cartoes" icon="▭" title="Cartões" />
        <ListRow to="/pix" icon="◈" title="Área PIX" />
        <ListRow to="/pagamentos" icon="▤" title="Pagamentos" />
        <ListRow to="/extrato" icon="≡" title="Extrato" />
      </RowGroup>
      <RowGroup>
        <ListRow to="/credito/cheque-especial" icon="⊕" title="Cheque especial" />
        <ListRow to="/credito/score" icon="★" title="Score de crédito" />
        <ListRow to="/beneficios" icon="✦" title="Benefícios Accio" />
        <ListRow to="/recarga" icon="▯" title="Recarga de celular" />
      </RowGroup>
      <Link to="/ajuda" className="rate-link">
        ♡ Avalie esta experiência
      </Link>
      <nav className="menu-footer" aria-label="Atalhos do menu">
        <Link to="/configurar" className="icon-btn" aria-label="Configurar">
          ⚙
        </Link>
        <Link to="/perfil" className="icon-btn" aria-label="Perfil">
          ☺
        </Link>
        <Link to="/ajuda" className="icon-btn" aria-label="Ajuda">
          ?
        </Link>
        <Link to="/seguranca" className="btn btn-ghost">
          ⛨ Segurança
        </Link>
      </nav>
    </div>
  )
}

/** Profile sheet: who is signed in, account data, sign out. */
export function ProfilePage() {
  const account = useAccount().data
  const unread = useUnreadCount().data?.count ?? 0
  const { logout } = useAuth()
  return (
    <div className="stack-lg">
      <PageHeader title="Perfil" back="/" />
      <div className="profile-head">
        <PhotoPicker name={account?.name} />
        <div>
          <h2>{account?.name}</h2>
          <p className="muted small">{account?.email}</p>
          <p className="muted small">
            Agência 0001 · Conta {account?.number} · {account?.tier}
          </p>
        </div>
      </div>
      <RowGroup>
        <ListRow to="/notificacoes" icon="🔔" title="Notificações" right={unread > 0 ? <span className="badge">{unread}</span> : undefined} />
        <ListRow to="/configurar" icon="⚙" title="Configurar" text="Cartão, conta, PIX, limites" />
        <ListRow to="/seguranca" icon="⛨" title="Segurança" text="Duas etapas, senha, dispositivos" />
      </RowGroup>
      <button className="list-row list-row-button" onClick={logout}>
        <span className="list-row-icon" aria-hidden>
          ⏻
        </span>
        <span className="list-row-body">
          <strong>Sair do aplicativo</strong>
        </span>
      </button>
    </div>
  )
}

export function SettingsPage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Configurar" back="/menu" />
      <RowGroup title="Serviços">
        <ListRow to="/cartoes" icon="▭" title="Configurar cartão" />
        <ListRow to="/perfil" icon="▣" title="Configurar conta" />
        <ListRow to="/pix/chaves" icon="◈" title="Configurar chaves PIX" />
        <ListRow to="/pix/limites" icon="◐" title="Limites do PIX" />
        <ListRow to="/credito/cheque-especial" icon="⊕" title="Limite do cheque especial" />
        <ListRow to="/investimentos" icon="▮" title="Arredondamento para cofres" />
      </RowGroup>
      <RowGroup title="Aplicativo">
        <ListRow to="/notificacoes" icon="🔔" title="Notificações" />
        <ListRow to="/seguranca" icon="⛨" title="Segurança" />
      </RowGroup>
    </div>
  )
}

const faq = [
  ['Como faço um PIX?', 'Na tela inicial, toque em "Área PIX e Transferir" e depois em "Pagar". Você pode usar chave, copia e cola ou QR Code.'],
  ['Por que meu PIX ficou em análise?', 'Por segurança, alguns PIX passam por uma análise rápida. O valor fica reservado e avisamos quando for liberado.'],
  ['Esqueci minha senha transacional', 'Em Segurança > Senha transacional você cria uma nova, confirmando com sua senha de acesso.'],
  ['Como funcionam os cofres?', 'Cofres rendem sobre o CDI todo dia útil e você resgata quando quiser. Dá para arredondar compras do cartão para um cofre.'],
  ['Como aumentar meu score?', 'Pagar em dia e movimentar a conta aumentam o score, recalculado todos os dias.'],
]

export function HelpPage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Ajuda" back="/" />
      <div className="help-hero">
        <Mascot mood="think" size={90} />
        <p>
          <strong>Como a coruja pode ajudar?</strong>
          <span className="muted small">Respostas rápidas para as dúvidas mais comuns.</span>
        </p>
      </div>
      <div className="faq">
        {faq.map(([q, a]) => (
          <details key={q} className="faq-item">
            <summary>{q}</summary>
            <p className="muted">{a}</p>
          </details>
        ))}
      </div>
      <p className="muted small">Banco fictício de portfólio: não há atendimento humano.</p>
    </div>
  )
}

const perks = [
  { title: 'Cashback no cartão', text: 'Parte das compras de volta na conta.', tag: 'Em breve' },
  { title: 'Cupons de parceiros', text: 'Descontos em lojas e apps parceiros.', tag: 'Em breve' },
  { title: 'Sorteios semanais', text: 'Números da sorte para quem guarda nos cofres.', tag: 'Em breve' },
  { title: 'Gift cards', text: 'Vales-presente com desconto.', tag: 'Em breve' },
]

/** Rewards area from the reference app. No backend yet: shown as "em breve". */
export function BenefitsPage() {
  return (
    <div className="stack-lg">
      <PageHeader title="Benefícios" />
      <div className="banner banner-static">
        <span>
          <strong>Benefícios Accio</strong>
          <span>Vantagens mágicas para clientes. Estamos preparando o feitiço.</span>
        </span>
        <Mascot mood="happy" size={70} />
      </div>
      <div className="perk-grid">
        {perks.map((p) => (
          <article key={p.title} className="perk">
            <span className="tag">{p.tag}</span>
            <h3>{p.title}</h3>
            <p className="muted small">{p.text}</p>
          </article>
        ))}
      </div>
      <RowGroup title="Enquanto isso">
        <ListRow to="/investimentos" icon="◫" title="Arredonde compras para um cofre" text="Cada compra no cartão guarda o troco" />
        <ListRow to="/credito/score" icon="★" title="Melhore seu score" />
      </RowGroup>
    </div>
  )
}

/** Tap the avatar to pick a photo (camera or gallery on phones); it is cropped to a square. */
function PhotoPicker({ name }: { name?: string }) {
  const photo = useAvatar()
  const { upload, remove } = usePhotoActions()
  const busy = upload.isPending || remove.isPending
  return (
    <div className="photo-picker">
      <label className={`avatar avatar-lg avatar-edit ${busy ? 'is-busy' : ''}`} title="Alterar foto" aria-busy={busy}>
        <AvatarFace name={name} />
        <span className="avatar-badge" aria-hidden>
          ✎
        </span>
        <input
          type="file"
          accept="image/*"
          className="sr-only"
          disabled={busy}
          aria-label={photo ? 'Alterar foto de perfil' : 'Adicionar foto de perfil'}
          onChange={(e) => {
            const file = e.target.files?.[0]
            e.target.value = ''
            if (file) upload.mutate(file)
          }}
        />
      </label>
      {photo && (
        <button className="btn btn-link small" disabled={busy} onClick={() => remove.mutate()}>
          Remover foto
        </button>
      )}
      <Alert kind="error">{upload.error?.message || remove.error?.message}</Alert>
    </div>
  )
}
