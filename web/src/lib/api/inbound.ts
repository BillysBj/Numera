// Inbound e-invoice (Eingangsbelege) BFF client (plan 05-05 → consumes the Api
// /api/inbound-documents surface; EINV-04/EINV-05).
//
// Same security posture as the other clients: the SPA holds NO tokens; auth rides the
// HttpOnly session cookie, so every call is same-origin with `credentials:'include'`.
//
// ENUM WIRE FORMAT: the Api has no JsonStringEnumConverter, so C# enums serialize as
// NUMBERS. The maps below mirror the server enum ordinals EXACTLY — a drift here silently
// breaks the validation-status badges and format labels.

import { API_BASE, ApiError } from '../api'

export { ApiError }

// ---- Enums (numeric wire values, mirroring the C# enums) -------------------

/**
 * Numera.Modules.Sales.EInvoice.EInvoiceValidationStatus — the canonical, append-only KoSIT
 * verdict. Accepted=0 / Rejected=1 / Unavailable=2 (a transient validator outage, DISTINCT
 * from a rejection). Mirrored here as fixed numbers.
 */
export const ValidationStatus = {
  Accepted: 0,
  Rejected: 1,
  Unavailable: 2,
} as const
export type ValidationStatus =
  (typeof ValidationStatus)[keyof typeof ValidationStatus]

/**
 * Numera.Modules.Sales.EInvoice.Inbound.InboundFormat — how the received e-invoice arrived:
 * a raw XRechnung UBL (0) or CII (1) XML, or a ZUGFeRD/Factur-X PDF (2) with embedded CII.
 */
export const InboundFormat = {
  XmlUbl: 0,
  XmlCii: 1,
  ZugferdPdf: 2,
} as const
export type InboundFormat = (typeof InboundFormat)[keyof typeof InboundFormat]

// ---- DTOs (match Numera.Api.Contracts.InboundContracts, camelCased) --------

/** One row of the inbound list (GET /api/inbound-documents). */
export interface InboundListItem {
  id: string
  detectedFormat: InboundFormat
  sellerName?: string | null
  sellerVatId?: string | null
  invoiceNumber?: string | null
  invoiceDate?: string | null
  totalGross?: number | null
  currency?: string | null
  validationStatus: ValidationStatus
  matchedPartnerId?: string | null
  originalFileName: string
  uploadedAt: string
}

export interface InboundListResponse {
  items: InboundListItem[]
  page: number
  pageSize: number
  total: number
}

/** One explained KoSIT finding (DE authoritative + EN). Mirrors EInvoiceFinding. */
export interface InboundFinding {
  severity: string
  ruleId?: string | null
  message: string
  explanationDe?: string | null
  explanationEn?: string | null
}

/** A party (seller/buyer) as parsed from the received document. */
export interface InboundParty {
  name?: string | null
  street?: string | null
  postalCode?: string | null
  city?: string | null
  countryCode?: string | null
  vatId?: string | null
  taxNumber?: string | null
}

/** One parsed line item (BG-25). */
export interface InboundReadLine {
  name?: string | null
  description?: string | null
  quantity?: number | null
  unitCode?: string | null
  netUnitPrice?: number | null
  lineNetAmount?: number | null
  taxCategory?: string | null
  vatRatePercent?: number | null
}

/** One parsed VAT breakdown row (BG-23). */
export interface InboundReadBreakdown {
  taxCategory?: string | null
  vatRatePercent?: number | null
  taxableBase?: number | null
  taxAmount?: number | null
  exemptionReasonCode?: string | null
  exemptionReasonText?: string | null
}

/** The human-readable parsed projection (mirrors InboundReadModel, camelCased). */
export interface InboundReadModel {
  invoiceNumber?: string | null
  invoiceDate?: string | null
  currency?: string | null
  seller: InboundParty
  buyer: InboundParty
  totalNet?: number | null
  totalTax?: number | null
  totalGross?: number | null
  amountDue?: number | null
  lines: InboundReadLine[]
  breakdownRows: InboundReadBreakdown[]
}

/** Full inbound detail (GET /api/inbound-documents/{id}). */
export interface InboundDetail {
  id: string
  detectedFormat: InboundFormat
  validationStatus: ValidationStatus
  matchedPartnerId?: string | null
  originalFileName: string
  originalContentType: string
  byteSize: number
  uploadedAt: string
  sellerName?: string | null
  sellerVatId?: string | null
  invoiceNumber?: string | null
  invoiceDate?: string | null
  totalGross?: number | null
  currency?: string | null
  readModel?: InboundReadModel | null
  findings: InboundFinding[]
}

/** The 201 response of an upload. */
export interface InboundUploadResult {
  id: string
  detectedFormat: InboundFormat
  validationStatus: ValidationStatus
  sellerName?: string | null
  invoiceNumber?: string | null
  totalGross?: number | null
  currency?: string | null
  matchedPartnerId?: string | null
}

// ---- fetch helper (same posture as lib/api.ts `request`) -------------------

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(init?.body && typeof init.body === 'string'
        ? { 'Content-Type': 'application/json' }
        : {}),
      ...init?.headers,
    },
    ...init,
  })

  if (!res.ok) {
    let body: unknown
    try {
      body = await res.json()
    } catch {
      body = await res.text().catch(() => undefined)
    }
    throw new ApiError(res.status, `Request failed: ${res.status}`, body)
  }

  if (res.status === 204) return undefined as T
  const text = await res.text()
  return (text ? JSON.parse(text) : undefined) as T
}

// ---- Inbound reads + upload ------------------------------------------------

export interface ListInboundParams {
  page?: number
  pageSize?: number
}

/** GET /api/inbound-documents — the paged, newest-first list of received e-invoices. */
export function listInboundDocuments(
  params: ListInboundParams = {},
): Promise<InboundListResponse> {
  const qs = new URLSearchParams()
  qs.set('page', String(params.page ?? 1))
  qs.set('pageSize', String(params.pageSize ?? 25))
  return request<InboundListResponse>(`/inbound-documents?${qs.toString()}`)
}

/** GET /api/inbound-documents/{id} — the human-readable detail + findings + match. */
export function getInboundDocument(id: string): Promise<InboundDetail> {
  return request<InboundDetail>(`/inbound-documents/${id}`)
}

/**
 * POST /api/inbound-documents — upload a received e-invoice (multipart). The browser sets the
 * multipart boundary, so no Content-Type header is passed. 201 with the parsed summary; 422
 * (ValidationProblem) when the file carries no e-invoice.
 */
export function uploadInboundDocument(file: File): Promise<InboundUploadResult> {
  const form = new FormData()
  form.append('file', file)
  return request<InboundUploadResult>('/inbound-documents', {
    method: 'POST',
    body: form,
  })
}

/**
 * GET /api/inbound-documents/{id}/original — download the immutable received bytes as-is (GoBD).
 * Returns the raw `Blob`; throws {@link ApiError} on non-2xx.
 */
export async function downloadInboundOriginal(id: string): Promise<Blob> {
  const res = await fetch(`${API_BASE}/inbound-documents/${id}/original`, {
    credentials: 'include',
  })
  if (!res.ok) {
    throw new ApiError(res.status, `Request failed: ${res.status}`, undefined)
  }
  return res.blob()
}
