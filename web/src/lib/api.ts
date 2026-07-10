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

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    // The HttpOnly session cookie carries auth — always send it.
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

// ---- Typed responses (server remains authoritative) -----------------------

export interface Me {
  userId: string
  email: string
  tenantId: string
  tenantName: string
}

export interface Entitlements {
  /** Cosmetic feature flags; the server enforces the real gates. */
  features: string[]
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
 */
export function loginUrl(): string {
  return `${API_BASE}/auth/login`
}
