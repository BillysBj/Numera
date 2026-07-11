// Partner management BFF client (plan 02-06 → consumes the 02-04 API).
//
// Same security posture as `lib/api.ts`: the SPA holds NO tokens; auth rides the
// HttpOnly session cookie, so every call is same-origin with `credentials:'include'`.
// This lives in its own file (not `lib/api.ts`) to keep the shared me/auth client
// churn-free while the partner + catalog features grow.
//
// ENUM WIRE FORMAT: the API has no JsonStringEnumConverter, so C# enums serialize
// as NUMBERS. The `*Value` maps below mirror the server enum ordinals exactly.

import { API_BASE, ApiError } from '../api'

export { ApiError }

// ---- Enums (numeric wire values, mirroring the C# enums) -------------------

/** Numera.Modules.Crm.PartnerLanguage — De=1, En=2. */
export const PartnerLanguage = { De: 1, En: 2 } as const
export type PartnerLanguage = (typeof PartnerLanguage)[keyof typeof PartnerLanguage]

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

/** Numera.Modules.Crm.PartnerActivityType — PartnerCreated=1 … NoteAdded=5. */
export const PartnerActivityType = {
  PartnerCreated: 1,
  PartnerUpdated: 2,
  Archived: 3,
  Unarchived: 4,
  NoteAdded: 5,
} as const
export type PartnerActivityType =
  (typeof PartnerActivityType)[keyof typeof PartnerActivityType]

// ---- DTOs (match Numera.Api.Contracts.PartnerContracts, camelCased) --------

export interface AddressDto {
  street: string
  line2?: string | null
  postalCode: string
  city: string
  countryCode: string
  poBox?: string | null
}

export interface PartnerListItem {
  id: string
  name: string
  isCustomer: boolean
  isSupplier: boolean
  city?: string | null
  archived: boolean
}

export interface PartnerListResponse {
  items: PartnerListItem[]
  page: number
  pageSize: number
  total: number
}

export interface PartnerDetail {
  id: string
  name: string
  legalForm?: string | null
  billingAddress: AddressDto
  shippingAddress?: AddressDto | null
  vatId?: string | null
  taxNumber?: string | null
  email?: string | null
  phone?: string | null
  website?: string | null
  paymentTermsNetDays?: number | null
  skontoPercent?: number | null
  skontoDays?: number | null
  defaultCurrency: string
  language: PartnerLanguage
  defaultTaxCategory?: TaxCategory | null
  isCustomer: boolean
  isSupplier: boolean
  customerNumber?: string | null
  supplierNumber?: string | null
  archived: boolean
}

export interface PartnerWriteRequest {
  name: string
  legalForm?: string | null
  billingAddress: AddressDto
  shippingAddress?: AddressDto | null
  vatId?: string | null
  taxNumber?: string | null
  email?: string | null
  phone?: string | null
  website?: string | null
  paymentTermsNetDays?: number | null
  skontoPercent?: number | null
  skontoDays?: number | null
  defaultCurrency: string
  language: PartnerLanguage
  defaultTaxCategory?: TaxCategory | null
  isCustomer: boolean
  isSupplier: boolean
  customerNumber?: string | null
  supplierNumber?: string | null
}

export interface ContactDto {
  id: string
  salutation?: string | null
  firstName?: string | null
  lastName: string
  email?: string | null
  phone?: string | null
  position?: string | null
  isPrimary: boolean
}

export interface ContactWriteRequest {
  salutation?: string | null
  firstName?: string | null
  lastName: string
  email?: string | null
  phone?: string | null
  position?: string | null
  isPrimary: boolean
}

export interface NoteDto {
  id: string
  authorUserId: string
  body: string
  createdAt: string
  updatedAt: string
}

export interface ActivityDto {
  id: string
  occurredAt: string
  type: PartnerActivityType
  summary: string
  actorUserId?: string | null
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

// ---- Partner CRUD + archive ------------------------------------------------

export interface ListPartnersParams {
  page?: number
  pageSize?: number
  q?: string
  role?: 'customer' | 'supplier' | ''
  archived?: boolean
}

/** GET /api/partners — server-side paged/filtered list ({items,page,pageSize,total}). */
export function listPartners(
  params: ListPartnersParams = {},
): Promise<PartnerListResponse> {
  const qs = new URLSearchParams()
  qs.set('page', String(params.page ?? 1))
  qs.set('pageSize', String(params.pageSize ?? 25))
  if (params.q) qs.set('q', params.q)
  if (params.role) qs.set('role', params.role)
  if (params.archived) qs.set('archived', 'true')
  return request<PartnerListResponse>(`/partners?${qs.toString()}`)
}

/** GET /api/partners/{id} — full detail (archived rows remain readable by id). */
export function getPartner(id: string): Promise<PartnerDetail> {
  return request<PartnerDetail>(`/partners/${id}`)
}

/** POST /api/partners — create; returns the new id. */
export function createPartner(
  body: PartnerWriteRequest,
): Promise<{ id: string }> {
  return request<{ id: string }>('/partners', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

/** PUT /api/partners/{id} — full replace; returns updated detail. */
export function updatePartner(
  id: string,
  body: PartnerWriteRequest,
): Promise<PartnerDetail> {
  return request<PartnerDetail>(`/partners/${id}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}

/** POST /api/partners/{id}/archive — soft-delete (never a hard delete). */
export function archivePartner(id: string): Promise<void> {
  return request<void>(`/partners/${id}/archive`, { method: 'POST' })
}

/** POST /api/partners/{id}/unarchive — reactivate. */
export function unarchivePartner(id: string): Promise<void> {
  return request<void>(`/partners/${id}/unarchive`, { method: 'POST' })
}

// ---- Contacts (BG-9) -------------------------------------------------------

export function listContacts(partnerId: string): Promise<ContactDto[]> {
  return request<ContactDto[]>(`/partners/${partnerId}/contacts`)
}

export function createContact(
  partnerId: string,
  body: ContactWriteRequest,
): Promise<{ id: string }> {
  return request<{ id: string }>(`/partners/${partnerId}/contacts`, {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function updateContact(
  partnerId: string,
  contactId: string,
  body: ContactWriteRequest,
): Promise<void> {
  return request<void>(`/partners/${partnerId}/contacts/${contactId}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}

export function deleteContact(
  partnerId: string,
  contactId: string,
): Promise<void> {
  return request<void>(`/partners/${partnerId}/contacts/${contactId}`, {
    method: 'DELETE',
  })
}

// ---- Notes (CRM-03) --------------------------------------------------------

export function listNotes(partnerId: string): Promise<NoteDto[]> {
  return request<NoteDto[]>(`/partners/${partnerId}/notes`)
}

export function createNote(
  partnerId: string,
  body: string,
): Promise<{ id: string }> {
  return request<{ id: string }>(`/partners/${partnerId}/notes`, {
    method: 'POST',
    body: JSON.stringify({ body }),
  })
}

export function updateNote(
  partnerId: string,
  noteId: string,
  body: string,
): Promise<void> {
  return request<void>(`/partners/${partnerId}/notes/${noteId}`, {
    method: 'PUT',
    body: JSON.stringify({ body }),
  })
}

export function deleteNote(partnerId: string, noteId: string): Promise<void> {
  return request<void>(`/partners/${partnerId}/notes/${noteId}`, {
    method: 'DELETE',
  })
}

// ---- Activity timeline (CRM-02 read) ---------------------------------------

export function listActivities(partnerId: string): Promise<ActivityDto[]> {
  return request<ActivityDto[]>(`/partners/${partnerId}/activities`)
}

/**
 * Extract server FluentValidation errors from an {@link ApiError} raised by a 400
 * ValidationProblem, as a `{ field: message }` map for react-hook-form `setError`.
 * Field keys arrive PascalCased (e.g. `BillingAddress.City`); callers lower-case
 * the first segment to match the zod/RHF field names.
 */
export function extractValidationErrors(
  err: unknown,
): Record<string, string> | null {
  if (!(err instanceof ApiError) || err.status !== 400) return null
  const body = err.body as { errors?: Record<string, string[]> } | undefined
  if (!body?.errors) return null
  const out: Record<string, string> = {}
  for (const [key, messages] of Object.entries(body.errors)) {
    if (messages?.length) out[key] = messages[0]
  }
  return out
}
