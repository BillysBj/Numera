import { useQuery } from '@tanstack/react-query'
import { API_BASE, ApiError } from '@/lib/api'

// ASP.NET serializes enums numerically in this API. Keep these values aligned with
// Numera.Modules.Ledger.Besteuerungsart (Soll = 0, Ist = 1).
export const Besteuerungsart = {
  Soll: 0,
  Ist: 1,
} as const
export type Besteuerungsart =
  (typeof Besteuerungsart)[keyof typeof Besteuerungsart]

export interface UstVaLine {
  kz: string
  bezeichnung: string
  bemessungsgrundlage: number | null
  steuer: number | null
  isComputed: boolean
  contributingAccountNumbers: string[]
}

export interface UstVaReport {
  jahr: number
  zeitraum: string
  besteuerungsart: Besteuerungsart
  isFestgeschrieben: boolean
  lines: UstVaLine[]
  zahllast: number
  hinweis: string | null
  isKleinunternehmer: boolean
}

export interface UstVaDrillDownEntry {
  kind: 'journal' | 'payment'
  journalEntryId: string | null
  journalNumber: string | null
  entryDate: string | null
  sourceRef: string | null
  description: string | null
  documentId: string | null
  documentNumber: string | null
  invoiceDate: string | null
  paymentId: string | null
  paymentValueDate: string | null
  paymentReference: string | null
  attributedNet: number
  attributedVat: number
}

export interface UstVaDrillDownResult {
  kz: string
  besteuerungsart: Besteuerungsart
  recognitionBasis: 'journalEntryDate' | 'paymentValueDate' | 'gated'
  isKleinunternehmer: boolean
  entries: UstVaDrillDownEntry[]
}

export interface EuerLine {
  zeile: string
  bezeichnung: string
  betrag: number
}

export interface EuerReport {
  jahr: number
  from: string
  to: string
  isKleinunternehmer: boolean
  betriebseinnahmen: EuerLine[]
  summeEinnahmen: number
  betriebsausgaben: EuerLine[]
  summeAusgaben: number
  gewinn: number
  isExpenseDataIncomplete: boolean
  hinweis: string | null
}

async function request<T>(path: string): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: { Accept: 'application/json' },
  })

  if (!response.ok) {
    let body: unknown
    try {
      body = await response.json()
    } catch {
      body = await response.text().catch(() => undefined)
    }
    throw new ApiError(response.status, `Request failed: ${response.status}`, body)
  }

  return (await response.json()) as T
}

function ustVaParams(jahr: number, zeitraum: string): string {
  return new URLSearchParams({ jahr: String(jahr), zeitraum }).toString()
}

function euerParams(jahr: number, from: string, to: string): string {
  return new URLSearchParams({ jahr: String(jahr), from, to }).toString()
}

export function useUstVaReport(jahr: number, zeitraum: string) {
  return useQuery({
    queryKey: ['reports', 'ustva', jahr, zeitraum],
    queryFn: () => request<UstVaReport>(`/reports/ustva?${ustVaParams(jahr, zeitraum)}`),
  })
}

export function useUstVaEntries(
  kz: string,
  jahr: number,
  zeitraum: string,
  enabled: boolean,
) {
  return useQuery({
    queryKey: ['reports', 'ustva', jahr, zeitraum, 'kz', kz, 'entries'],
    queryFn: () =>
      request<UstVaDrillDownResult>(
        `/reports/ustva/kz/${encodeURIComponent(kz)}/entries?${ustVaParams(jahr, zeitraum)}`,
      ),
    enabled,
  })
}

export function useEuerReport(jahr: number, from: string, to: string) {
  return useQuery({
    queryKey: ['reports', 'euer', jahr, from, to],
    queryFn: () => request<EuerReport>(`/reports/euer?${euerParams(jahr, from, to)}`),
    enabled: from.length > 0 && to.length > 0 && from <= to,
  })
}

function filenameFromHeader(header: string | null, fallback: string): string {
  const match = header?.match(/filename\*?=(?:UTF-8''|\")?([^\";]+)/i)
  if (!match?.[1]) return fallback
  try {
    return decodeURIComponent(match[1].trim())
  } catch {
    return match[1].trim()
  }
}

async function download(path: string, fallbackFilename: string): Promise<void> {
  const response = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: { Accept: '*/*' },
  })

  if (!response.ok) {
    let body: unknown
    try {
      body = await response.json()
    } catch {
      body = await response.text().catch(() => undefined)
    }
    throw new ApiError(response.status, `Download failed: ${response.status}`, body)
  }

  const objectUrl = URL.createObjectURL(await response.blob())
  const anchor = document.createElement('a')
  anchor.href = objectUrl
  anchor.download = filenameFromHeader(
    response.headers.get('Content-Disposition'),
    fallbackFilename,
  )
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(objectUrl)
}

export function downloadUstVaXml(jahr: number, zeitraum: string): Promise<void> {
  return download(
    `/reports/ustva/export.xml?${ustVaParams(jahr, zeitraum)}`,
    `ustva-${jahr}-${zeitraum}.xml`,
  )
}

export function downloadUstVaPdf(jahr: number, zeitraum: string): Promise<void> {
  return download(
    `/reports/ustva/export.pdf?${ustVaParams(jahr, zeitraum)}`,
    `ustva-${jahr}-${zeitraum}.pdf`,
  )
}

export function downloadEuerPdf(
  jahr: number,
  from: string,
  to: string,
): Promise<void> {
  return download(
    `/reports/euer/export.pdf?${euerParams(jahr, from, to)}`,
    `euer-${jahr}.pdf`,
  )
}
