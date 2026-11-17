import { useState } from 'react'
import { Alert, Amount, BankCardVisual, Button, Card, Field, PageHeader, Select } from '../components/ui'
import { Logo, Mascot } from '../components/Mascot'

// Living reference of the AccioBank design system: every sample here uses the real tokens and components.
const colors = [
  ['--bg', 'Fundo'],
  ['--surface', 'Superfície'],
  ['--surface-2', 'Superfície 2'],
  ['--text', 'Texto'],
  ['--muted', 'Texto secundário'],
  ['--brand', 'Marca (ouro)'],
  ['--brand-text', 'Marca em texto'],
  ['--peach', 'Ouro escuro'],
  ['--accent', 'Vinho'],
  ['--ink', 'Ink'],
  ['--in', 'Entrada'],
  ['--out', 'Saída'],
  ['--info', 'Informação'],
]

export default function DesignSystemPage() {
  const [tab, setTab] = useState('a')

  return (
    <main className="content content-wide stack-lg">
      <PageHeader title="AccioBank · Design System" />
      <p className="muted">Tokens, tipografia e componentes do app. Tema noturno: céu estrelado, pergaminho, ouro e vinho. Títulos em Cinzel.</p>

      <Card className="stack">
        <h2>Cores</h2>
        <div className="ds-swatches">
          {colors.map(([token, name]) => (
            <div key={token} className="ds-swatch">
              <i style={{ background: `var(${token})` }} />
              <strong>{name}</strong>
              <span className="mono muted">{token}</span>
            </div>
          ))}
        </div>
      </Card>

      <Card className="stack">
        <h2>Tipografia — Cinzel + Manrope</h2>
        <h1>Título 1 · 28/800</h1>
        <h2>Título 2 · 18/700</h2>
        <p>Corpo · 16/400 — o dinheiro só se move pelo razão contábil.</p>
        <p className="muted small">Legenda · 14 · texto secundário</p>
        <p className="big">R$ 12.480,90</p>
        <p className="mono">0000 1111 2222 3333 · JetBrains Mono</p>
      </Card>

      <Card className="stack">
        <h2>Cartões</h2>
        <div className="ds-cards">
          <BankCardVisual label="Débito · Físico" number="•••• •••• 4821" holder="Carla Souza" expiry="08/31" />
          <BankCardVisual credit label="Crédito · Físico" number="•••• •••• 7304" holder="Carla Souza" expiry="11/31" />
          <BankCardVisual virtual label="Débito · Virtual" number="•••• •••• 0192" holder="Carla Souza" expiry="02/28" />
          <BankCardVisual blocked label="Bloqueado" number="•••• •••• 5566" holder="Carla Souza" expiry="05/30" />
        </div>
      </Card>

      <Card className="stack">
        <h2>Mascote · a coruja</h2>
        <div className="row" style={{ alignItems: 'end', gap: 32 }}>
          <Mascot mood="hello" size={130} />
          <Mascot mood="happy" size={130} />
          <Mascot mood="think" size={130} />
          <Logo />
        </div>
        <p className="muted small">Uma ilustração, três movimentos: hello (flutua), happy (pulinho), think (inclina a cabeça, usado em dicas e estados vazios). Recorte do rosto no logo e no favicon.</p>
      </Card>

      <div className="grid-auto">
        <Card className="stack">
          <h2>Botões</h2>
          <div className="row">
            <Button>Primário</Button>
            <Button variant="ghost">Secundário</Button>
            <Button variant="danger">Perigo</Button>
            <Button variant="link">Link</Button>
            <Button busy>Primário</Button>
          </div>
          <div className="chips">
            <button className="chip chip-active">Chip ativo</button>
            <button className="chip">Chip</button>
          </div>
          <div className="tabs" role="tablist">
            {['a', 'b', 'c'].map((t) => (
              <button key={t} role="tab" aria-selected={tab === t} className={tab === t ? 'tab-active' : ''} onClick={() => setTab(t)}>
                Aba {t.toUpperCase()}
              </button>
            ))}
          </div>
        </Card>

        <Card className="stack">
          <h2>Formulários</h2>
          <Field label="Valor" placeholder="R$ 0,00" hint="Dica abaixo do campo" />
          <Select label="Tipo de chave">
            <option>CPF</option>
            <option>E-mail</option>
          </Select>
        </Card>

        <Card className="stack">
          <h2>Feedback</h2>
          <Alert kind="success">PIX enviado.</Alert>
          <Alert kind="info">Seu limite noturno é R$ 1.000,00.</Alert>
          <Alert kind="error">Saldo insuficiente.</Alert>
          <ul className="list">
            <li>
              <span>Salário</span>
              <Amount value={5200} />
            </li>
            <li>
              <span>Mercado</span>
              <Amount value={-184.3} />
            </li>
          </ul>
        </Card>
      </div>
    </main>
  )
}
