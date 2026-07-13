// Company-profile (issuer / Ausstellerstammdaten) BFF client (plan 03-10 → consumes the
// 03-01 GET/PUT /api/company-profile API).
//
// This is the §14 UStG issuer master data — legal name, billing address, VAT/tax identity,
// §19 Kleinunternehmer status, default payment terms + bank/registry — that plan 03-05
// (finalize) FREEZES onto invoices. PUT is an UPSERT: it creates the single per-tenant
// profile on the first write and updates it thereafter.
//
// Same security posture as `lib/api/catalog.ts`: the SPA holds NO tokens; auth rides the
// HttpOnly session cookie, so every call is same-origin with `credentials:'include'`. Enums
// cross the wire as NUMBERS (no JsonStringEnumConverter server-side).

import { API_BASE, ApiError } from '../api'
import { TaxCategory } from './catalog'

export { ApiError }
// Re-export TaxCategory for the defaultTaxCategory select (mirrors the catalog client).
export { TaxCategory }
// Re-export the shared 400-ValidationProblem extractor so the settings form maps server
// field errors identically to the partner/catalog forms (same {field: message} shape).
export { extractValidationErrors } from './partners'

// ---- DTOs (match Numera.Api.Contracts.CompanyProfileContracts, camelCased) --

/** A postal address (EN 16931 BG-5). Mirrors the shared C# AddressDto. */
export interface AddressDto {
  street: string
  line2?: string | null
  postalCode: string
  city: string
  countryCode: string
  poBox?: string | null
}

/**
 * The tenant's issuer profile (GET /api/company-profile). All fields are nullable so the
 * settings form gets an editable empty state when no profile exists yet — the server
 * returns an all-nulls shell (Address null) rather than a 404 on the first read.
 */
export interface CompanyProfileDto {
  legalName?: string | null
  address?: AddressDto | null
  vatId?: string | null
  taxNumber?: string | null
  isKleinunternehmer: boolean
  defaultPaymentTermsNetDays?: number | null
  defaultTaxCategory?: TaxCategory | null
  iban?: string | null
  bic?: string | null
  bankName?: string | null
  registerCourt?: string | null
  registerNumber?: string | null
  managingDirector?: string | null
  contactEmail?: string | null
  contactPhone?: string | null
  logoRef?: string | null
}

/** Create-or-update payload (PUT /api/company-profile — upsert). The writable shape. */
export interface UpdateCompanyProfileRequest {
  legalName: string
  address: AddressDto
  vatId?: string | null
  taxNumber?: string | null
  isKleinunternehmer: boolean
  defaultPaymentTermsNetDays?: number | null
  defaultTaxCategory?: TaxCategory | null
  iban?: string | null
  bic?: string | null
  bankName?: string | null
  registerCourt?: string | null
  registerNumber?: string | null
  managingDirector?: string | null
  contactEmail?: string | null
  contactPhone?: string | null
  logoRef?: string | null
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

// ---- Company-profile read + upsert -----------------------------------------

/**
 * GET /api/company-profile — the tenant's issuer profile. The server returns an all-nulls
 * editable shell when none exists yet; we normalise a fully-empty shell (no legal name AND
 * no address) to `null` so the form can distinguish "first use" from a persisted profile.
 */
export async function getCompanyProfile(): Promise<CompanyProfileDto | null> {
  const dto = await request<CompanyProfileDto | null>('/company-profile')
  if (dto == null) return null
  const isEmptyShell =
    !dto.legalName && dto.address == null && !dto.vatId && !dto.taxNumber
  return isEmptyShell ? null : dto
}

/** PUT /api/company-profile — upsert; returns the persisted profile. */
export function updateCompanyProfile(
  body: UpdateCompanyProfileRequest,
): Promise<CompanyProfileDto> {
  return request<CompanyProfileDto>('/company-profile', {
    method: 'PUT',
    body: JSON.stringify(body),
  })
}
