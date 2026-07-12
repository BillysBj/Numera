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
  netUnitPrice: number
  lineNetAmount: number
  taxCategory: TaxCategory
  vatRatePercent: number
}

/** One VAT breakdown row of a document detail (BG-23). */
export interface SalesTaxBreakdown {
  id: string
  taxCategory: TaxCategory
  vatRatePercent: number
  taxableBase: number
  taxAmount: number
  exemptionReasonCode?: string | null
  exemptionReasonText?: string | null
}

/** Full document projection for the detail/edit view (GET /api/documents/{id}). */
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
  totalNet: number
  totalTax: number
  totalGross: number
  amountDue: number
  isKleinunternehmer: boolean
  reverseCharge: boolean
  buyerReference?: string | null
  notes?: string | null
  sourceDocumentId?: string | null
  correctsDocumentId?: string | null
  cancelledByDocumentId?: string | null
  lines: SalesLine[]
  taxBreakdown: SalesTaxBreakdown[]
}

/** One requested line — a SNAPSHOT the client supplies (usually from the CATL-02 picker). */
export interface SalesLineRequest {
  catalogItemId?: string | null
  name: string
  description?: string | null
  quantity: number
  unitCode: string
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
