// BFF API client.
//
// SECURITY POSTURE (RESEARCH Pattern 5 / Pitfall 6):
//   The SPA is a public client and stores NO tokens. Authentication rides on
//   an HttpOnly session cookie set by the BFF; JavaScript can neither read nor
//   set it. Every request therefore uses `credentials: 'include'` and talks to
//   the BFF at the SAME ORIGIN (`/api/*`) so the cookie is sent automatically.
//   There is intentionally no localStorage/sessionStorage token handling here.

/** Base path of the BFF. Same-origin so the session cookie flows on fetch. */
export const API_BASE = '/api'

export class ApiError extends Error {
  readonly status: number
  readonly body?: unknown

  constructor(status: number, message: string, body?: unknown) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    // The HttpOnly session cookie carries auth — always send it.
    credentials: 'include',
    headers: {
      Accept: 'application/json',
      ...(typeof init?.body === 'string' ? { 'Content-Type': 'application/json' } : {}),
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

/** Fetch an authenticated same-origin API payload that is intentionally binary. */
export async function apiBlob(path: string, init?: RequestInit): Promise<Blob> {
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: 'include',
    headers: { Accept: '*/*', ...init?.headers },
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

  return res.blob()
}

const request = apiRequest

// ---- Typed responses (server remains authoritative) -----------------------

/** Mirrors the nested shape of `GET /api/me` (user + tenant + role). */
export interface Me {
  user: { sub: string | null; email: string | null; name: string | null }
  /** Null only in the (handled) no-tenant-in-scope case. `plan` is the tier string, e.g. "L". */
  tenant: { id: string; name: string; plan: string } | null
  /** Membership role string, e.g. "Owner" | "Employee" | "TaxAdvisor". */
  role: string | null
}

export interface Entitlements {
  /** Cosmetic capability names; the server enforces the real gates. `GET /api/me/entitlements`. */
  capabilities: string[]
}

export interface RegisterCompanyRequest {
  companyName: string
  email: string
  password: string
}

// ---- Endpoints ------------------------------------------------------------

/** Current authenticated principal + tenant. `GET /api/me`. */
export function getMe(): Promise<Me> {
  return request<Me>('/me')
}

/** Feature entitlements for the current tenant. `GET /api/me/entitlements`. */
export function getEntitlements(): Promise<Entitlements> {
  return request<Entitlements>('/me/entitlements')
}

export interface Dashboard {
  revenueYear: number
  openItemsTotal: number
  overdueTotal: number
  documentsThisMonth: number
  revenueByMonth: { month: string; net: number }[]
  aging: { notDue: number; d1_30: number; d31_60: number; d60Plus: number }
  docStatus: { draft: number; finalized: number; paid: number; cancelled: number }
}

/** Overview aggregates for the dashboard. `GET /api/dashboard`. */
export function getDashboard(): Promise<Dashboard> {
  return request<Dashboard>('/dashboard')
}

/** Register a new company + owner account. `POST /api/auth/register`. */
export function registerCompany(body: RegisterCompanyRequest): Promise<void> {
  return request<void>('/auth/register', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

/**
 * Start the BFF OIDC challenge. This is a full-page navigation (not fetch): the
 * BFF redirects to the identity provider and sets the session cookie on return.
 * `returnUrl` is the SPA origin so the user lands back on the app (not the API
 * origin) after the Keycloak round-trip; the host-scoped session cookie applies.
 */
export function loginUrl(): string {
  const returnUrl =
    typeof window !== 'undefined' ? `${window.location.origin}/dashboard` : '/dashboard'
  return `${API_BASE}/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`
}
