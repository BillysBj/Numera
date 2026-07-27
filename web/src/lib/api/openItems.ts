// Open-items (offene Posten / OP-Übersicht) BFF client (plan 03-10 → consumes the 03-04
// GET /api/open-items API — the read half of OPDN-01).
//
// Same security posture as `lib/api/catalog.ts` / `lib/api/documents.ts`: the SPA holds
// NO tokens; auth rides the HttpOnly session cookie, so every call is same-origin with
// `credentials:'include'`. Open items are read-only here — they are CREATED at invoice
// finalize (03-05) and reduced by payments (Phase 6); neither is exposed.
//
// ENUM WIRE FORMAT: the API has no JsonStringEnumConverter, so C# enums serialize as
// NUMBERS. The map below mirrors the server enum ordinals exactly.

import { API_BASE, ApiError } from '../api'

export { ApiError }

// ---- Enums (numeric wire values, mirroring the C# enums) -------------------

/**
 * Numera.Modules.Sales.OpenItemStatus — declaration order. v1 only ever creates `Open`
 * items (and `Cancelled` on a Storno); PartiallyPaid/Paid land in Phase 6.
 */
export const OpenItemStatus = {
  Open: 0,
  PartiallyPaid: 1,
  Paid: 2,
  Cancelled: 3,
} as const
export type OpenItemStatus =
  (typeof OpenItemStatus)[keyof typeof OpenItemStatus]

// ---- DTOs (match Numera.Api.Contracts.OpenItemContracts, camelCased) -------

/** One row of the OP-Übersicht (GET /api/open-items). */
export interface OpenItemListItem {
  id: string
  documentId: string
  documentNumber: string
  partnerId?: string | null
  currency: string
  originalAmount: number
  openAmount: number
  status: OpenItemStatus
  issuedOn: string
  dueDate: string
  /** Server-computed: the due date has passed and the item is still (partially) open. */
  overdue: boolean
  currentDunningLevel: number
  lastDunnedOn?: string | null
}

export interface OpenItemListResponse {
  items: OpenItemListItem[]
  page: number
  pageSize: number
  total: number
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

// ---- Open-items read -------------------------------------------------------

export interface ListOpenItemsParams {
  page?: number
  pageSize?: number
  status?: OpenItemStatus | null
  overdueOnly?: boolean
}

/**
 * GET /api/open-items — the paged, due-date-sorted OP-Übersicht ({items,page,pageSize,total}),
 * filterable by settlement status and an overdue-only toggle. Read-only (OPDN-01 read half).
 */
export function listOpenItems(
  params: ListOpenItemsParams = {},
): Promise<OpenItemListResponse> {
  const qs = new URLSearchParams()
  qs.set('page', String(params.page ?? 1))
  qs.set('pageSize', String(params.pageSize ?? 25))
  if (params.status != null) qs.set('status', String(params.status))
  if (params.overdueOnly) qs.set('overdueOnly', 'true')
  return request<OpenItemListResponse>(`/open-items?${qs.toString()}`)
}
