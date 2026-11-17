// Dates come from the API in UTC and are always shown in Brasília time.
const TZ = 'America/Sao_Paulo'

const brlFormat = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
const dateTimeFormat = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short', timeZone: TZ })
const dateFormat = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeZone: TZ })
const dayFormat = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: 'long', timeZone: TZ })

export const brl = (value: number) => brlFormat.format(value)
export const dateTime = (iso: string) => dateTimeFormat.format(new Date(iso))
export const date = (iso: string) => dateFormat.format(new Date(iso))
export const dayLabel = (iso: string) => dayFormat.format(new Date(iso))

/** "2026-12-10" (date-only from the API) -> "10/12/2026", without timezone shifts. */
export const dateOnly = (value: string) => value.split('-').reverse().join('/')

/** Today in Brasília as yyyy-mm-dd, for date inputs. */
export function todayInBrasilia(): string {
  const parts = new Intl.DateTimeFormat('en-CA', { timeZone: TZ, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
  return parts
}

/** Accepts "1.234,56", "1234.56" or "1234,5"; returns NaN when it is not a number. */
export function parseAmount(input: string): number {
  const clean = input.replace(/[R$\s]/g, '')
  const normalized = clean.includes(',') ? clean.replace(/\./g, '').replace(',', '.') : clean
  return normalized === '' ? NaN : Number(normalized)
}

export function maskCpf(input: string): string {
  const d = input.replace(/\D/g, '').slice(0, 11)
  return d
    .replace(/^(\d{3})(\d)/, '$1.$2')
    .replace(/^(\d{3})\.(\d{3})(\d)/, '$1.$2.$3')
    .replace(/\.(\d{3})(\d{1,2})$/, '.$1-$2')
}

export function maskPhone(input: string): string {
  const d = input.replace(/\D/g, '').slice(0, 11)
  if (d.length <= 2) return d
  if (d.length <= 7) return `(${d.slice(0, 2)}) ${d.slice(2)}`
  return `(${d.slice(0, 2)}) ${d.slice(2, d.length - 4)}-${d.slice(-4)}`
}

export function maskCep(input: string): string {
  const d = input.replace(/\D/g, '').slice(0, 8)
  return d.length > 5 ? `${d.slice(0, 5)}-${d.slice(5)}` : d
}

export const keyTypeLabel: Record<string, string> = { Cpf: 'CPF', Email: 'E-mail', Phone: 'Celular', Random: 'Aleatória' }

export const kindLabel: Record<string, string> = {
  Deposit: 'Depósito',
  InternalTransfer: 'Transferência',
  Pix: 'PIX',
  PixRefund: 'Devolução de PIX',
}
