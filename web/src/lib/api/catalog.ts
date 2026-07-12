// Catalog (Artikelstamm) management BFF client (plan 02-07 → consumes the 02-05 API).
//
// Same security posture as `lib/api.ts` / `lib/api/partners.ts`: the SPA holds NO
// tokens; auth rides the HttpOnly session cookie, so every call is same-origin with
// `credentials:'include'`. Lives in its own file to keep the shared me/auth client
// (and the partner client) churn-free.
//
// ENUM WIRE FORMAT: the API has no JsonStringEnumConverter, so C# enums serialize as
// NUMBERS. The maps below mirror the server enum ordinals exactly.

import { API_BASE, ApiError } from '../api'

export { ApiError }
// Re-export the shared 400-ValidationProblem extractor from the partner client so the
// catalog form maps server errors onto fields identically (same {field: message} shape).
export { extractValidationErrors } from './partners'

// ---- Enums (numeric wire values, mirroring the C# enums) -------------------

/** Numera.Modules.Catalog.CatalogItemKind — Product=1, Service=2. */
export const CatalogItemKind = { Product: 1, Service: 2 } as const
export type CatalogItemKind =
  (typeof CatalogItemKind)[keyof typeof CatalogItemKind]

/** Numera.Platform.Money.TaxCategory — declaration order (S=0 … O=6). */
export const TaxCategory = {
  S: 0,
  AE: 1,
  K: 2,
  E: 3,
  Z: 4,
  G: 5,
  O: 6,
} as const
export type TaxCategory = (typeof TaxCategory)[keyof typeof TaxCategory]

// ---- DTOs (match Numera.Api.Contracts.CatalogContracts, camelCased) --------

/** Compact projection for the paged catalog list (GET /api/catalog-items). */
export interface CatalogListItem {
  id: string
  itemNumber: string
  name: string
  kind: CatalogItemKind
  unitCode: string
  netPrice: number
  taxCategory: TaxCategory
  vatRatePercent?: number | null
  archived: boolean
}

export interface CatalogListResponse {
  items: CatalogListItem[]
  page: number
  pageSize: number
  total: number
}

/** Full catalog item projection for the detail/edit view (GET /api/catalog-items/{id}). */
export interface CatalogItemDetail {
  id: string
  itemNumber: string
  name: string
  description?: string | null
  kind: CatalogItemKind
  unitCode: string
  netPrice: number
  currency: string
  taxCategory: TaxCategory
  vatRatePercent?: number | null
  costPrice?: number | null
  archived: boolean
}

/** Create/update payload — the server treats update as a full replace. */
export interface CatalogWriteRequest {
  itemNumber: string
  name: string
  description?: string | null
  kind: CatalogItemKind
  unitCode?: string | null
  netPrice: number
  currency: string
  taxCategory: TaxCategory
  vatRatePercent?: number | null
  costPrice?: number | null
}

/**
 * The CATL-02 seam — the stable "usable as an invoice line" projection of a catalog
 * item, returned by GET /api/catalog-items?picker=true. Phase-3's invoice line editor
 * imports {@link lookupCatalogItems} and SNAPSHOTS these values onto the line at
 * creation; the catalog is NOT the source of truth once a line exists. Keep additive.
 */
export interface CatalogLineItem {
  id: string
  itemNumber: string
  name: string
  unitCode: string
  netPrice: number
  taxCategory: TaxCategory
  vatRatePercent?: number | null
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

// ---- Catalog CRUD + archive ------------------------------------------------

export interface ListCatalogItemsParams {
  page?: number
  pageSize?: number
  q?: string
  archived?: boolean
}

/** GET /api/catalog-items — server-side paged/searchable list ({items,page,pageSize,total}). */
export function listCatalogItems(
  params: ListCatalogItemsParams = {},
): Promise<CatalogListResponse> {
  const qs = new URLSearchParams()
  qs.set('page', String(params.page ?? 1))
  qs.set('pageSize', String(params.pageSize ?? 25))
  if (params.q) qs.set('q', params.q)
  if (params.archived) qs.set('archived', 'true')
  return request<CatalogListResponse>(`/catalog-items?${qs.toString()}`)
}

/** GET /api/catalog-items/{id} — full detail (archived rows remain readable by id). */
export function getCatalogItem(id: string): Promise<CatalogItemDetail> {
  return request<CatalogItemDetail>(`/catalog-items/${id}`)
}

/** POST /api/catalog-items — create; returns the new id. */
export function createCatalogItem(
  body: CatalogWriteRequest,
): Promise<{ id: string }> {
  return request<{ id: string }>('/catalog-items', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

/** PUT /api/catalog-items/{id} — full replace; returns updated detail. */
export function updateCatalogItem(
  id: string,
  body: CatalogWriteRequest,
): Promise<CatalogItemDetail> {
  return request<CatalogItemDetail>(`/catalog-items/${id}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}

/** POST /api/catalog-items/{id}/archive — soft-delete (never a hard delete). */
export function archiveCatalogItem(id: string): Promise<void> {
  return request<void>(`/catalog-items/${id}/archive`, { method: 'POST' })
}

/** POST /api/catalog-items/{id}/unarchive — reactivate. */
export function unarchiveCatalogItem(id: string): Promise<void> {
  return request<void>(`/catalog-items/${id}/unarchive`, { method: 'POST' })
}

/**
 * GET /api/catalog-items?picker=true — the CATL-02 picker seam. Returns a capped list
 * of {@link CatalogLineItem} (non-archived, matching `q`) for the Phase-3 invoice line
 * editor to import and snapshot onto a line. Exported now so the Phase-3 contract is
 * fixed at the frontend boundary.
 */
export function lookupCatalogItems(q?: string): Promise<CatalogLineItem[]> {
  const qs = new URLSearchParams({ picker: 'true' })
  if (q) qs.set('q', q)
  return request<CatalogLineItem[]>(`/catalog-items?${qs.toString()}`)
}
