// Sales-document (Belege) management BFF client (plan 03-07 → consumes the 03-04 API).
//
// Same security posture as `lib/api/catalog.ts` / `lib/api/partners.ts`: the SPA holds
// NO tokens; auth rides the HttpOnly session cookie, so every call is same-origin with
// `credentials:'include'`. Lives in its own file to keep the shared me/auth client (and
// the partner + catalog clients) churn-free.
//
// ENUM WIRE FORMAT: the API has no JsonStringEnumConverter, so C# enums serialize as
// NUMBERS. The maps below mirror the server enum ordinals exactly.

import { API_BASE, ApiError } from '../api'
import { TaxCategory } from './catalog'

export { ApiError }
// Re-export so the invoice line editor snapshots catalog picks onto document lines
// through the fixed CATL-02 seam without importing two modules.
export { TaxCategory }
export type { CatalogLineItem } from './catalog'
export { lookupCatalogItems } from './catalog'
// Re-export the shared 400-ValidationProblem extractor from the partner client so the
// document form maps server errors onto fields identically (same {field: message} shape).
export { extractValidationErrors } from './partners'

// ---- Enums (numeric wire values, mirroring the C# enums) -------------------

/**
 * Numera.Modules.Sales.DocumentType — declaration order. The polymorphic
 * sales_documents discriminator (Angebot → Auftragsbestätigung → Lieferschein →
 * Rechnung chain, plus Storno/Gutschrift corrections).
 */
export const DocumentType = {
  Angebot: 0,
  Auftragsbestaetigung: 1,
  Lieferschein: 2,
  Rechnung: 3,
  Storno: 4,
  Gutschrift: 5,
  Abschlagsrechnung: 6,
  Schlussrechnung: 7,
} as const
export type DocumentType = (typeof DocumentType)[keyof typeof DocumentType]

/** Numera.Modules.Sales.DocumentStatus — Draft=0 is load-bearing (the DB triggers key off it). */
export const DocumentStatus = {
  Draft: 0,
  Finalized: 1,
  Sent: 2,
  Cancelled: 3,
  Paid: 4,
} as const
export type DocumentStatus = (typeof DocumentStatus)[keyof typeof DocumentStatus]

/**
 * Numera.Modules.Sales.EmailStatus — the send-status the POST /{id}/send endpoint returns
 * for the freshly-queued document_email row (Queued=0; the async Hangfire job advances it to
 * Sent/Failed). Numeric ordinals cross the wire (no JsonStringEnumConverter server-side).
 */
export const EmailStatus = {
  Queued: 0,
  Sent: 1,
  Failed: 2,
} as const
export type EmailStatus = (typeof EmailStatus)[keyof typeof EmailStatus]

// ---- DTOs (match Numera.Api.Contracts.SalesDocumentContracts, camelCased) --

/** Compact projection for the paged document list (GET /api/documents). */
export interface SalesDocumentListItem {
  id: string
  documentType: DocumentType
  status: DocumentStatus
  documentNumber?: string | null
  partnerId?: string | null
  documentDate: string
  totalGross: number
  currency: string
}

export interface SalesDocumentListResponse {
  items: SalesDocumentListItem[]
  page: number
  pageSize: number
  total: number
}

/** One line of a document detail (BG-25). */
export interface SalesLine {
  id: string
  lineNumber: number
  catalogItemId?: string | null
  name: string
  description?: string | null
  quantity: number
  unitCode: string
  discountPercent?: number
  netUnitPrice: number
  lineNetAmount: number
  taxCategory: TaxCategory
  vatRatePercent: number
}

/**
 * One VAT breakdown row of a document detail (BG-23). The persisted, server-computed
 * truth that the finalize step froze — the detail page RENDERS this, it never recomputes.
 */
export interface SalesTaxBreakdownRow {
  id: string
  taxCategory: TaxCategory
  vatRatePercent: number
  taxableBase: number
  taxAmount: number
  exemptionReasonCode?: string | null
  /** The mandatory German Pflichttext for exempt/reverse-charge categories (AE/K/E/G). */
  exemptionReasonText?: string | null
}
/** @deprecated Use {@link SalesTaxBreakdownRow}. Kept as an alias to avoid churn. */
export type SalesTaxBreakdown = SalesTaxBreakdownRow

/**
 * Full document projection for the detail/edit view (GET /api/documents/{id}).
 *
 * This is the read model the DETAIL page renders (plan 03-09). Snapshot discipline,
 * numbering, immutability and VAT are all SERVER-side truth (03-02/03-05/03-06) — the
 * page displays what finalize froze, it never recomputes any of it.
 */
export interface SalesDocumentDetail {
  id: string
  documentType: DocumentType
  status: DocumentStatus
  documentNumber?: string | null
  partnerId?: string | null
  documentDate: string
  serviceDate?: string | null
  servicePeriodEnd?: string | null
  dueDate?: string | null
  currency: string
  exchangeRate?: number | null
  exchangeRateDate?: string | null
  totalTaxEur?: number | null
  totalNet: number
  totalTax: number
  totalGross: number
  amountDue: number
  isKleinunternehmer: boolean
  reverseCharge: boolean
  buyerReference?: string | null
  notes?: string | null
  finalizedAt?: string | null
  sentAt?: string | null
  // Chain links (the Belegkette): where this document came from / what it corrects /
  // what cancelled it. Rendered as cross-links on the detail page when set.
  sourceDocumentId?: string | null
  correctsDocumentId?: string | null
  cancelledByDocumentId?: string | null
  lines: SalesLine[]
  taxBreakdown: SalesTaxBreakdownRow[]
  prepayments: SalesDocumentPrepayment[]
  // Frozen §14 issuer + recipient snapshots (jsonb). The server MAY expose these as a
  // nested object or a JSON string (or omit them from the leaner projection); typed as
  // `unknown` and read through `parseSnapshot` so the page never depends on the exact
  // serialization. NEVER fill these from a live partner/company read — the snapshot is
  // the source of truth (RESEARCH Pitfall 2).
  issuerSnapshot?: unknown
  recipientSnapshot?: unknown
}

export interface SalesDocumentPrepayment {
  abschlagDocumentId: string
  abschlagNumber: string
  abschlagDate: string
  netAmount: number
  vatAmount: number
  grossAmount: number
}

/**
 * Read a frozen §14 snapshot defensively regardless of how the server serialized the
 * jsonb: parse a JSON string, pass a nested object straight through, else null. The page
 * then reads issuer/recipient fields (legalName, address, vatId/taxNumber …) off the
 * resulting record without assuming a fixed shape.
 */
export function parseSnapshot(v: unknown): Record<string, unknown> | null {
  if (v == null) return null
  if (typeof v === 'string') {
    const s = v.trim()
    if (!s) return null
    try {
      const parsed = JSON.parse(s)
      return parsed && typeof parsed === 'object'
        ? (parsed as Record<string, unknown>)
        : null
    } catch {
      return null
    }
  }
  if (typeof v === 'object') return v as Record<string, unknown>
  return null
}

/** One requested line — a SNAPSHOT the client supplies (usually from the CATL-02 picker). */
export interface SalesLineRequest {
  catalogItemId?: string | null
  name: string
  description?: string | null
  quantity: number
  unitCode: string
  discountPercent?: number
  netUnitPrice: number
  taxCategory: TaxCategory
  vatRatePercent: number
}

/** Create/update payload — the server treats update as a full replace of header + lines. */
export interface CreateSalesDocumentRequest {
  documentType: DocumentType
  partnerId?: string | null
  documentDate: string
  serviceDate?: string | null
  notes?: string | null
  buyerReference?: string | null
  lines: SalesLineRequest[]
  currency?: string | null
  exchangeRate?: number | null
  exchangeRateDate?: string | null
}

export interface FinalInvoiceRequest
  extends Omit<CreateSalesDocumentRequest, 'documentType'> {
  abschlagDocumentIds: string[]
}

// ---- fetch helper (same posture as lib/api.ts `request`) -------------------

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
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

// ---- Document CRUD + convert -----------------------------------------------

export interface ListSalesDocumentsParams {
  page?: number
  pageSize?: number
  q?: string
  type?: DocumentType | null
  status?: DocumentStatus | null
  partnerId?: string | null
}

/** GET /api/documents — server-side paged/filterable list ({items,page,pageSize,total}). */
export function listSalesDocuments(
  params: ListSalesDocumentsParams = {},
): Promise<SalesDocumentListResponse> {
  const qs = new URLSearchParams()
  qs.set('page', String(params.page ?? 1))
  qs.set('pageSize', String(params.pageSize ?? 25))
  if (params.q) qs.set('q', params.q)
  if (params.type != null) qs.set('type', String(params.type))
  if (params.status != null) qs.set('status', String(params.status))
  if (params.partnerId) qs.set('partnerId', params.partnerId)
  return request<SalesDocumentListResponse>(`/documents?${qs.toString()}`)
}

/** GET /api/documents/{id} — full detail incl. lines, breakdown and chain links. */
export function getSalesDocument(id: string): Promise<SalesDocumentDetail> {
  return request<SalesDocumentDetail>(`/documents/${id}`)
}

/** POST /api/documents — create a Draft; returns the new id. */
export function createSalesDocument(
  body: CreateSalesDocumentRequest,
): Promise<{ id: string }> {
  return request<{ id: string }>('/documents', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function createAbschlag(
  body: CreateSalesDocumentRequest,
): Promise<{ id: string }> {
  return request<{ id: string }>('/documents/abschlag', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function createFinalInvoice(
  body: FinalInvoiceRequest,
): Promise<{ id: string }> {
  return request<{ id: string }>('/documents/final-invoice', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function listAbschlaege(
  partnerId: string,
): Promise<SalesDocumentListResponse> {
  return listSalesDocuments({
    pageSize: 100,
    type: DocumentType.Abschlagsrechnung,
    status: DocumentStatus.Finalized,
    partnerId,
  })
}

/** PUT /api/documents/{id} — full replace of a Draft (409 if the document is no longer a Draft). */
export function updateSalesDocument(
  id: string,
  body: CreateSalesDocumentRequest,
): Promise<void> {
  return request<void>(`/documents/${id}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}

/** DELETE /api/documents/{id} — hard-delete a Draft (409 if the document is no longer a Draft). */
export function deleteSalesDocument(id: string): Promise<void> {
  return request<void>(`/documents/${id}`, { method: 'DELETE' })
}

/**
 * POST /api/documents/{id}/convert — copy-forward the chain (DOCS-01). Creates a NEW
 * Draft of `targetType` copying the source header + lines; returns the new id. Any source
 * status converts freely (a finalized Angebot can become a Rechnung draft).
 */
export function convertSalesDocument(
  id: string,
  targetType: DocumentType,
): Promise<{ id: string }> {
  return request<{ id: string }>(`/documents/${id}/convert`, {
    method: 'POST',
    body: JSON.stringify({ targetType }),
  })
}

// ---- Lifecycle: finalize / storno / credit-note (03-05 + 03-06) ------------

/**
 * POST /api/documents/{id}/finalize — the core v1 transaction (03-05). Runs the §14 gate,
 * freezes the issuer + recipient snapshots, persists the BG-23 breakdown + frozen totals,
 * assigns the race-safe legal number and (for a Rechnung) opens the open item, then flips
 * the document to Finalized — IRREVERSIBLE. Returns the finalized detail (number +
 * breakdown + snapshots). 409 if the document is not a Draft; 422 (ValidationProblem) if
 * §14 is incomplete (missing company profile, or an AE/K line without a recipient VatId).
 */
export function finalizeSalesDocument(id: string): Promise<SalesDocumentDetail> {
  return request<SalesDocumentDetail>(`/documents/${id}/finalize`, {
    method: 'POST',
  })
}

/**
 * POST /api/documents/{id}/storno — cancel a finalized Rechnung (03-06). Creates a
 * finalized negative-mirror Storno (EN 16931 type 384) with its own number and flips the
 * original to Cancelled (cancelled_by → the Storno). Returns the new Storno's id +
 * documentNumber. 409 if the source is not a finalized invoice / already cancelled.
 */
export function stornoSalesDocument(
  id: string,
): Promise<{ id: string; documentNumber?: string }> {
  return request<{ id: string; documentNumber?: string }>(
    `/documents/${id}/storno`,
    { method: 'POST' },
  )
}

/**
 * POST /api/documents/{id}/credit-note — create a kaufmännische Gutschrift (type 381) Draft
 * referencing the original (positive amounts — a credit note is NOT a negative invoice).
 * The draft is then edited + finalized through the normal /finalize path. Returns its id.
 */
export function createCreditNote(id: string): Promise<{ id: string }> {
  return request<{ id: string }>(`/documents/${id}/credit-note`, {
    method: 'POST',
  })
}

// ---- PDF download + e-mail send (04-03 / 04-04 surfaces) --------------------

/**
 * GET /api/documents/{id}/pdf?lang=de|en — download the finalized document's §14 PDF (04-03).
 * The server returns the stored render or renders-on-demand from the frozen snapshot, so a
 * download never fails on a not-yet-async-rendered document. Returns the raw `Blob` (the
 * shared JSON `request` helper is bypassed on purpose); throws {@link ApiError} on non-2xx.
 * 409 when the document is still a Draft (no frozen snapshot), 404 when unknown under RLS.
 */
export async function downloadDocumentPdf(
  id: string,
  lang: 'de' | 'en' = 'de',
): Promise<Blob> {
  const res = await fetch(`${API_BASE}/documents/${id}/pdf?lang=${lang}`, {
    credentials: 'include',
    headers: { Accept: 'application/pdf' },
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
  return res.blob()
}

/**
 * GET /api/documents/{id}/xrechnung?syntax=ubl|cii — download the finalized invoice's XRechnung
 * (EN 16931, render-if-absent from the frozen snapshot). Returns the raw XML `Blob`. Server-gated
 * on the EInvoicing capability (403 without it — plan L+); 409 Draft, 404 unknown under RLS.
 */
export async function downloadXRechnung(
  id: string,
  syntax: 'ubl' | 'cii' = 'ubl',
): Promise<Blob> {
  const res = await fetch(`${API_BASE}/documents/${id}/xrechnung?syntax=${syntax}`, {
    credentials: 'include',
    headers: { Accept: 'application/xml' },
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
  return res.blob()
}

/**
 * GET /api/documents/{id}/zugferd — download the finalized invoice's ZUGFeRD PDF/A-3 (a valid
 * e-invoice AND a human-readable PDF). Returns the raw PDF `Blob`. Server-gated on EInvoicing
 * (403 without it); 409 Draft, 404 unknown.
 */
export async function downloadZugferd(id: string): Promise<Blob> {
  const res = await fetch(`${API_BASE}/documents/${id}/zugferd`, {
    credentials: 'include',
    headers: { Accept: 'application/pdf' },
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
  return res.blob()
}

/**
 * POST /api/documents/{id}/send-einvoice — the authoritative stage-2 send gate: dispatches the
 * stored XRechnung only if KoSIT Accepted it. Records a Queued `document_email` + enqueues the
 * send job. Server-gated on EInvoicing (403); 409 Draft/not-validated; 422 rejected/no recipient.
 */
export function sendEInvoice(
  id: string,
  body: SendDocumentEmailRequest = {},
): Promise<{ id: string; status: EmailStatus }> {
  return request<{ id: string; status: EmailStatus }>(
    `/documents/${id}/send-einvoice`,
    { method: 'POST', body: JSON.stringify(body) },
  )
}

/** Optional overrides for {@link sendDocumentEmail}. */
export interface SendDocumentEmailRequest {
  /** Override recipient address; omit → the frozen recipient e-mail on the document. */
  toAddress?: string
  /** Covering-mail language (de/en); omit → de. The PDF's legal content is unaffected. */
  language?: 'de' | 'en'
}

/**
 * POST /api/documents/{id}/send — e-mail the finalized document's PDF to the customer (04-04).
 * The endpoint records a Queued `document_email` row and enqueues the async send job, then
 * returns 202 with the row id + status (Queued). 409 if the document is a Draft; 404 if
 * unknown; 422 (ValidationProblem) when no recipient e-mail is available.
 */
export function sendDocumentEmail(
  id: string,
  body: SendDocumentEmailRequest = {},
): Promise<{ id: string; status: EmailStatus }> {
  return request<{ id: string; status: EmailStatus }>(`/documents/${id}/send`, {
    method: 'POST',
    body: JSON.stringify(body),
  })
}
