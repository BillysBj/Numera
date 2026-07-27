// Payments BFF client. Authentication is carried by the same-origin HttpOnly
// session cookie; the SPA never handles credentials or access tokens itself.

import { API_BASE, ApiError } from '../api'

export { ApiError }

// ENUM WIRE FORMAT: C# enums are serialized as numbers. Keep these ordinals in
// lockstep with PaymentMethod.cs; changing either side independently silently
// changes the meaning of persisted payment data.
export const PaymentMethod = {
  BankTransfer: 0,
  Cash: 1,
  Card: 2,
  Sepa: 3,
  Other: 4,
} as const
export type PaymentMethod =
  (typeof PaymentMethod)[keyof typeof PaymentMethod]

export interface RecordPaymentRequest {
  amount: number
  valueDate: string
  method: PaymentMethod
  reference?: string
  allocations: { openItemId: string; amount: number }[]
}

export interface PaymentListItem {
  id: string
  amount: number
  valueDate: string
  method: PaymentMethod
  reference?: string | null
  reversesPaymentId?: string | null
  recordedAt: string
}

export interface PaymentListResponse {
  items: PaymentListItem[]
  page: number
  pageSize: number
  total: number
}

export interface ListPaymentsParams {
  page?: number
  pageSize?: number
  openItemId?: string
}

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

export function recordPayment(
  body: RecordPaymentRequest,
): Promise<{ id: string }> {
  return request<{ id: string }>('/payments', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function reversePayment(id: string): Promise<{ id: string }> {
  return request<{ id: string }>(`/payments/${id}/reverse`, {
    method: 'POST',
  })
}

export function listPayments(
  params: ListPaymentsParams = {},
): Promise<PaymentListResponse> {
  const qs = new URLSearchParams()
  qs.set('page', String(params.page ?? 1))
  qs.set('pageSize', String(params.pageSize ?? 25))
  if (params.openItemId) qs.set('openItemId', params.openItemId)
  return request<PaymentListResponse>(`/payments?${qs.toString()}`)
}
